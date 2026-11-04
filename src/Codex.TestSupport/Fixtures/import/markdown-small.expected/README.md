# Expected prose for `markdown-small`

The stored `prose.body` the Markdown importer writes for each scene of `../markdown-small`, by `import_ref`. Beat ids
are replaced in order by `0199c0de-0000-7000-8000-0000000000b1`, `…b2`, … `ImportWriteTests` compares against these
files, and `importProse.test.ts` checks each one is canonical (`serialize(parse(x)) === x`).
