// Types that break (or keep) the feature-isolation rule on purpose. They live in the test assembly, which the
// architecture tests do not scan as production, so the rule can be shown to fail on a violation.
namespace Codex.Application.Features.Zebra
{
    public class ZebraHelper
    {
        public int Number { get; set; } = 1;
    }

    public class ZebraNeighbour
    {
        private int _reads;

        public int Same()
        {
            _reads++;
            return new ZebraHelper().Number + _reads;
        }
    }
}

namespace Codex.Application.Features.Yak
{
    public class YakByField
    {
        public Zebra.ZebraHelper? Held { get; set; }
    }

    public class YakByCall
    {
        private int _reads;

        public int Read()
        {
            _reads++;
            return new Zebra.ZebraHelper().Number + _reads;
        }
    }

    public class YakByGeneric
    {
        private int _calls;

        public IReadOnlyList<int> Many()
        {
            _calls++;
            return new List<Zebra.ZebraHelper> { new() }.Select(z => z.Number + _calls).ToList();
        }
    }
}
