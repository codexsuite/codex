using System.Reflection;
using Codex.Contracts;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace Codex.WebApi.Infrastructure;

// Controllers answer exactly as the minimal APIs they replaced (#98), without touching Program.cs (PostConfigure, so
// this runs after AddControllers has added its formatters):
// - the services own validation and its messages (ApiException.Validation), so MVC adds no implicit [Required] for
//   non-nullable reference types;
// - a null result is 200 with no body, as Results.Ok(null) was, not 204 or the text `null`;
// - a client error result (415 and friends) stays bodiless, so ApiErrorServices gives it the ApiError body rather than
//   MVC writing ProblemDetails;
// - an action with a JSON body only matches application/json, as a minimal API's body parameter did, so any other
//   content type falls through to the /api fallback's 404 (an optional body also matches a request with no body);
// - a blank route or query string reaches the service as itself ("" or " "), not null, so the service still refuses it.
public sealed class MvcServices : IServiceModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.PostConfigure<MvcOptions>(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            options.OutputFormatters.RemoveType<HttpNoContentOutputFormatter>();
            options.OutputFormatters.Insert(0, new NullAsEmptyBodyFormatter());
            options.Conventions.Add(new JsonBodyConsumesConvention());
            options.ModelMetadataDetailsProviders.Add(new KeepBlankStringsProvider());
        });
        services.PostConfigure<ApiBehaviorOptions>(options => options.SuppressMapClientErrors = true);
    }

    // Writes nothing for a null object, so the status stays 200 and there is no body or content type.
    private sealed class NullAsEmptyBodyFormatter : IOutputFormatter
    {
        public bool CanWriteResult(OutputFormatterCanWriteContext context) => context.Object is null;

        public Task WriteAsync(OutputFormatterWriteContext context) => Task.CompletedTask;
    }

    private sealed class KeepBlankStringsProvider : IDisplayMetadataProvider
    {
        public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
            => context.DisplayMetadata.ConvertEmptyStringToNull = false;
    }

    // What a minimal API's body parameter declared (AcceptsMetadata): JSON only, optional when the parameter is nullable or
    // [FromBody(EmptyBodyBehavior = Allow)]. An action with its own [Consumes] (the minimal API's .Accepts(...)) keeps it.
    private sealed class JsonBodyConsumesConvention : IActionModelConvention
    {
        public void Apply(ActionModel action)
        {
            var body = action.Parameters.FirstOrDefault(p => p.BindingInfo?.BindingSource == BindingSource.Body);
            if (body is null || action.Attributes.OfType<IAcceptsMetadata>().Any())
            {
                return;
            }
            var consumes = new ConsumesAttribute("application/json") { IsOptional = IsOptional(body) };
            foreach (var selector in action.Selectors)
            {
                selector.EndpointMetadata.Add(consumes);
            }
        }

        private static bool IsOptional(ParameterModel body)
            => body.BindingInfo?.EmptyBodyBehavior == EmptyBodyBehavior.Allow
               || body.ParameterInfo.HasDefaultValue
               || Nullable.GetUnderlyingType(body.ParameterType) is not null
               || new NullabilityInfoContext().Create(body.ParameterInfo).ReadState == NullabilityState.Nullable;
    }
}
