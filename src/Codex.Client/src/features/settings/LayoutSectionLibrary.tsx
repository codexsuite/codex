import { useRef, useState } from 'react';
import type { LayoutTemplate } from '@codex/core/api/layout';
import { layoutTemplatesApi } from '@codex/core/api/layoutTemplates';
import { FieldRow } from './FieldRow';
import { ForkTemplateDialog } from './ForkTemplateDialog';
import { TemplateThumb } from './TemplateThumb';
import { settingsTestIds } from './testIds';

interface LayoutSectionLibraryProps {
  current: string;
  templates: LayoutTemplate[];
  onUse: (slug: string) => void;
  /** Runs a change to the template library (fork, upload, archive), then refreshes Layout. Rejects with the server's message. */
  onChange: (change: () => Promise<unknown>) => Promise<void>;
}

const button =
  'h-7 cursor-pointer rounded border border-border bg-surface px-2.5 text-sm text-text no-underline disabled:cursor-default disabled:opacity-50';

// Every template the book can use, as cards (behind Show templates, so the section stays compact): built-ins, then the
// user's own. A card shows the thumbnail (sample.md in
// the template's look), the version this book uses, where a fork came from, and its actions: Use, Fork…, Download, and
// for the user's own Upload new version… and Archive (the server refuses while a book uses it). Upload template (.zip)…
// adds a new template from a package.
export function LayoutSectionLibrary({
  current,
  templates,
  onUse,
  onChange,
}: LayoutSectionLibraryProps) {
  const fileRef = useRef<HTMLInputElement>(null);
  const [uploadTo, setUploadTo] = useState<LayoutTemplate | null>(null);
  const [forking, setForking] = useState<LayoutTemplate | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const forkVersion = forking?.versionId;

  const run = async (change: () => Promise<unknown>) => {
    setError(null);
    try {
      await onChange(change);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'That did not work.');
    }
  };

  const pickFile = (target: LayoutTemplate | null) => {
    setUploadTo(target);
    fileRef.current?.click();
  };

  const uploaded = (file: File | undefined) => {
    if (!file) return;
    const target = uploadTo;
    void run(() =>
      target?.templateId
        ? layoutTemplatesApi.uploadVersion(target.templateId, file)
        : layoutTemplatesApi.upload(file),
    );
    if (fileRef.current) fileRef.current.value = '';
  };

  return (
    <FieldRow
      label="Templates"
      help="Built-in templates and yours. Fork one to change its class or CSS, download it, edit, and upload it as a new version."
    >
      <button
        type="button"
        data-testid={settingsTestIds.layoutTemplatesToggle}
        aria-expanded={open}
        className={button}
        onClick={() => setOpen(!open)}
      >
        {open ? 'Hide templates' : `Show templates (${templates.length})`}
      </button>
      {open && (
        <ul
          data-testid={settingsTestIds.layoutTemplates}
          className="m-0 flex basis-full list-none flex-col gap-2 p-0"
        >
          {templates.map((t) => (
            <li
              key={t.slug}
              data-testid={settingsTestIds.layoutTemplateCard(t.slug)}
              aria-current={t.slug === current}
              className="flex items-start gap-3 rounded-md border border-border bg-bg px-2.5 py-2 aria-[current=true]:border-accent"
            >
              {t.versionId ? (
                <TemplateThumb slug={t.slug} versionId={t.versionId} name={t.name} />
              ) : (
                <div className="h-[57px] w-[44px] shrink-0 rounded-sm bg-white ring-1 ring-black/10" />
              )}
              <div className="flex min-w-0 flex-1 flex-col gap-0.5">
                <b className="text-sm font-semibold text-text">
                  {t.name}
                  {t.isUserTemplate ? ' (yours)' : ''}
                </b>
                <span className="text-xs text-muted">
                  v{t.version} · {t.trims.join(' · ')}
                  {t.hasPreviewCss ? '' : ' · no preview.css'}
                  {t.latestVersion ? ` · v${t.latestVersion} available` : ''}
                </span>
                {t.forkedFrom && (
                  <span className="text-xs text-muted">Forked from {t.forkedFrom}</span>
                )}
                {!t.forkedFrom && t.description && (
                  <span className="text-xs text-muted">{t.description}</span>
                )}
                <div className="mt-1 flex flex-wrap gap-1.5">
                  {t.slug === current ? (
                    <span className="h-7 px-1 text-sm leading-7 text-accent">In use</span>
                  ) : (
                    <button
                      type="button"
                      data-testid={settingsTestIds.layoutTemplateUse(t.slug)}
                      className={button}
                      onClick={() => onUse(t.slug)}
                    >
                      Use
                    </button>
                  )}
                  {t.versionId && (
                    <>
                      <button
                        type="button"
                        data-testid={settingsTestIds.layoutTemplateFork(t.slug)}
                        className={button}
                        onClick={() => setForking(t)}
                      >
                        Fork…
                      </button>
                      <a
                        data-testid={settingsTestIds.layoutTemplateDownload(t.slug)}
                        href={layoutTemplatesApi.packageUrl(t.versionId)}
                        download
                        className={`${button} inline-flex items-center`}
                      >
                        Download
                      </a>
                    </>
                  )}
                  {t.isUserTemplate && t.templateId && (
                    <>
                      <button
                        type="button"
                        data-testid={settingsTestIds.layoutTemplateUploadVersion(t.slug)}
                        className={button}
                        onClick={() => pickFile(t)}
                      >
                        Upload new version…
                      </button>
                      <button
                        type="button"
                        data-testid={settingsTestIds.layoutTemplateArchive(t.slug)}
                        className={button}
                        disabled={t.slug === current}
                        title={
                          t.slug === current
                            ? 'This book uses it. Choose another template first.'
                            : undefined
                        }
                        onClick={() => {
                          const id = t.templateId;
                          if (id) void run(() => layoutTemplatesApi.archive(id));
                        }}
                      >
                        Archive
                      </button>
                    </>
                  )}
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
      <button
        type="button"
        data-testid={settingsTestIds.layoutTemplateUpload}
        className={button}
        onClick={() => pickFile(null)}
      >
        Upload template (.zip)…
      </button>
      <input
        ref={fileRef}
        type="file"
        accept=".zip,application/zip"
        hidden
        data-testid={settingsTestIds.layoutTemplateFile}
        onChange={(e) => uploaded(e.target.files?.[0])}
      />
      {error && (
        <p
          data-testid={settingsTestIds.layoutTemplateError}
          role="alert"
          className="m-0 basis-full text-sm whitespace-pre-line text-danger"
        >
          {error}
        </p>
      )}
      {forking && forkVersion && (
        <ForkTemplateDialog
          template={forking}
          onClose={() => setForking(null)}
          onFork={async (name) => {
            await onChange(() => layoutTemplatesApi.fork(forkVersion, name));
            setForking(null);
          }}
        />
      )}
    </FieldRow>
  );
}
