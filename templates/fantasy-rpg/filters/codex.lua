-- Fantasy RPG: Codex directives (docs/prose-format.md, issue #13) drawn with the rpgtex environments.
-- The assembler emits template-neutral Divs; anything this filter does not handle falls back to pandoc's default.
local latex = FORMAT:match('latex') ~= nil

local function raw(text) return pandoc.RawBlock('latex', text) end
local function inline(text) return pandoc.RawInline('latex', text) end
local function tex(inlines)
  local text = pandoc.write(pandoc.Pandoc({ pandoc.Plain(inlines) }), 'latex'):gsub('\n$', '')
  return text
end

-- Typographic punctuation, as pandoc's +smart reads it (the Players Guide was built with markdown+smart): a straight
-- quote curls by what comes before it, -- and --- become en and em dashes, ... an ellipsis. It lives in this template,
-- not the assembler, because typography is the template's choice (the manuscript template keeps typewriter quotes).
-- It also keeps pandoc's \textquotesingle and \textquotedbl out of the LaTeX: those are TS1 glyphs, and Bookman's bold
-- TS1 font (pbkd8c) is not in the warmed Tectonic cache, so "**Porters' Rebellion:**" stopped the compile.
local opening = { [''] = true, ['('] = true, ['['] = true, ['{'] = true, ['‘'] = true, ['“'] = true, ['—'] = true,
  ['–'] = true, ['-'] = true, ['/'] = true }

-- prev is the character before text: '' at the start of a block or after a space.
local function curl_text(text, prev)
  local chars = {}
  for _, code in utf8.codes(text) do chars[#chars + 1] = utf8.char(code) end
  local out, i = {}, 1
  while i <= #chars do
    local c, step = chars[i], 1
    if c == '-' and chars[i + 1] == '-' then
      if chars[i + 2] == '-' then c, step = '—', 3 else c, step = '–', 2 end
    elseif c == '.' and chars[i + 1] == '.' and chars[i + 2] == '.' then
      c, step = '…', 3
    elseif c == "'" then
      local after = chars[i + 1]
      c = (opening[prev] and not (after and after:match('^[%s%d]$'))) and '‘' or '’'
    elseif c == '"' then
      c = opening[prev] and '“' or '”'
    end
    out[#out + 1] = c
    prev = c
    i = i + step
  end
  return table.concat(out), prev
end

local curl_filter

-- Curls one list of inlines in reading order, carrying the character before each Str across formatting.
local function curl(inlines, prev)
  for _, el in ipairs(inlines) do
    local t = el.t
    if t == 'Str' then
      el.text, prev = curl_text(el.text, prev)
    elseif t == 'Space' or t == 'SoftBreak' or t == 'LineBreak' then
      prev = ''
    elseif t == 'Note' then
      el.content = el.content:walk(curl_filter)
    elseif t == 'Code' or t == 'RawInline' or t == 'Math' then
      prev = 'x'
    elseif el.content then -- Emph, Strong, Span, Link, Image (alt text), Quoted and the rest
      prev = curl(el.content, prev)
    end
  end
  return prev
end

curl_filter = { traverse = 'topdown', Inlines = function(inlines) curl(inlines, ''); return inlines, false end }

-- Plain text as LaTeX: every string interpolated into a command goes through here, so text never becomes markup.
local function esc(text) return tex({ pandoc.Str((curl_text(text or '', ''))) }) end

local dropcaps = true
local onecolumn = false

local function wrap(env, opts, div)
  local blocks = { raw('\\begin{' .. env .. '}' .. opts) }
  for _, b in ipairs(div.content) do table.insert(blocks, b) end
  table.insert(blocks, raw('\\end{' .. env .. '}'))
  return blocks
end

local function without(blocks, classes)
  local kept = pandoc.List()
  for _, b in ipairs(blocks) do
    local skip = false
    if b.t == 'Div' then
      for _, class in ipairs(classes) do skip = skip or b.classes:includes(class) end
    end
    if not skip then kept:insert(b) end
  end
  return kept
end

local function title_of(div)
  local inlines = pandoc.Inlines(div.attributes['title'] or '')
  curl(inlines, '')
  return tex(inlines)
end

local function field(div, name) return esc(div.attributes['data-field-' .. name]) end

local function Div(div)
  if not latex then return nil end
  local c = div.classes
  if c:includes('readaloud') then return wrap('DndReadAloud', '', div) end
  if c:includes('sidebar') then return wrap('DndSidebar', '{' .. title_of(div) .. '}', div) end
  if c:includes('aside') then return wrap('DndComment', '{' .. title_of(div) .. '}', div) end
  if c:includes('pagebreak') then return raw('\\clearpage') end
  if c:includes('missing-figure') then
    return raw('\\CodexMissingFigure{' .. tex(pandoc.utils.blocks_to_inlines(div.content)) .. '}')
  end
  -- The assembler's fallback look (Div.entry-heading, Div.entry-fields) repeats what the header or box title draws.
  if c:includes('spell') then
    local blocks = { raw(string.format('\\DndSpellHeader{%s}{%s}{%s}{%s}{%s}{%s}',
      title_of(div), field(div, 'level-school'), field(div, 'casting-time'), field(div, 'range'),
      field(div, 'components'), field(div, 'duration'))) }
    for _, b in ipairs(without(div.content, { 'entry-heading', 'entry-fields' })) do table.insert(blocks, b) end
    return blocks
  end
  if c:includes('statblock') then
    div.content = without(div.content, { 'entry-heading' })
    return wrap('DndSidebar', '{' .. title_of(div) .. '}', div)
  end
  return nil
end

-- A pipe table as a DndTable (#64): the header row is bold, columns are left (X), centred (Y) or right-aligned, and every
-- cell goes through pandoc.write so bold, italics, code and links survive (never pandoc.utils.stringify).
local function cell_tex(cell, bold)
  local text = pandoc.write(pandoc.Pandoc(cell.contents), 'latex'):gsub('%s+$', '')
  if bold and text ~= '' then text = '\\textbf{' .. text .. '}' end
  return text
end

local function row_tex(row, bold)
  local cells = {}
  for _, cell in ipairs(row.cells) do table.insert(cells, cell_tex(cell, bold)) end
  return table.concat(cells, ' & ') .. ' \\\\'
end

local function column_tex(spec)
  local align = spec[1]
  if align == 'AlignCenter' then return 'Y' end
  if align == 'AlignRight' then return '>{\\raggedleft\\arraybackslash}X' end
  return 'X'
end

local function Table(tbl)
  if not latex then return nil end
  local columns = {}
  for _, spec in ipairs(tbl.colspecs) do table.insert(columns, column_tex(spec)) end
  local lines = { '\\begin{DndTable}{' .. table.concat(columns) .. '}' }
  for _, row in ipairs(tbl.head.rows) do table.insert(lines, row_tex(row, true)) end
  for _, body in ipairs(tbl.bodies) do
    for _, row in ipairs(body.body) do table.insert(lines, row_tex(row, false)) end
  end
  table.insert(lines, '\\end{DndTable}')
  return raw(table.concat(lines, '\n'))
end

-- Full-width placements span both columns; a missing image never reaches here (the assembler swaps it out).
local function Figure(fig)
  if not latex then return nil end
  local c = fig.classes
  if c:includes('page') or c:includes('spread') or c:includes('float-top') then
    local body = pandoc.write(pandoc.Pandoc({ fig }), 'latex')
    body = body:gsub('\\begin{figure}', '\\begin{figure*}'):gsub('\\end{figure}', '\\end{figure*}')
    return raw(body)
  end
  return nil
end

-- A part or chapter opener (act/chapter opener_asset_id). A part's title and art share one page, as the Players
-- Guide drew them (\part inside a one-column [p] figure); a chapter's art gets its own page before the heading.
local function opener(header)
  local art = header.attributes['opener']
  if not art then return { header } end
  local cap = header.attributes['opener-caption'] or ''
  -- art is the assembler's own images/<hex>.<ext> path; the caption is author text.
  local image = '\\includegraphics[width=\\textwidth,height=%s,keepaspectratio]{' .. art .. '}'
    .. (cap ~= '' and ('\\caption*{' .. esc(cap) .. '}') or '')
  if header.level == 1 then
    return {
      raw((onecolumn and '\\clearpage' or '\\onecolumn') .. '\n\\begin{figure}[p]\\centering'),
      header,
      raw(string.format(image, '0.8\\textheight') .. '\n\\end{figure}\n' .. (onecolumn and '\\clearpage' or '\\twocolumn')),
    }
  end
  return { raw('\\begin{figure*}[p]\\centering' .. string.format(image, '\\textheight') .. '\\end{figure*}'), header }
end

-- Drop caps on the first paragraph after each chapter heading (template option drop-caps), past the chapter's
-- subheadings ("Fighter", "Overview", then the paragraph).
local function Blocks(blocks)
  if not latex then return nil end
  local out, pending = {}, false
  for _, b in ipairs(blocks) do
    if b.t == 'Header' then
      for _, o in ipairs(opener(b)) do table.insert(out, o) end
      if b.level <= 2 then pending = dropcaps and b.level == 2 end
    elseif pending and b.t == 'Para' and b.content[1] and b.content[1].t == 'Str' and utf8.len(b.content[1].text) > 1
      and b.content[1].text:match('^%w') then -- the initials fonts have letters and digits only
      local word = b.content[1].text
      local first = word:sub(1, utf8.offset(word, 2) - 1)
      local rest = word:sub(utf8.offset(word, 2))
      local inlines = { inline('\\DndDropCapLine{' .. esc(first) .. '}{' .. esc(rest) .. '}') }
      for i = 2, #b.content do table.insert(inlines, b.content[i]) end
      table.insert(out, pandoc.Para(inlines))
      pending = false
    else
      table.insert(out, b)
      if b.t ~= 'Div' then pending = false end
    end
  end
  return out
end

-- Meta runs first so the options are known before the blocks are drawn.
-- ulem's \sout (the wrapper's \st) cannot hold a footnote, so notes inside struck text follow the struck text.
local function Strikeout(strike)
  local notes = pandoc.List()
  local walked = strike:walk({ Note = function(note) notes:insert(note); return {} end })
  if #notes == 0 then return nil end
  return pandoc.List({ walked }) .. notes
end

return {
  { Meta = function(meta)
    if meta['drop-caps'] ~= nil then dropcaps = meta['drop-caps'] end
    onecolumn = meta['onecolumn'] == true
  end },
  { Strikeout = Strikeout },
  curl_filter,
  -- pandoc writes a line break as \\ and a newline, so a next line starting with * read as \\* and lost its star.
  { LineBreak = function() if latex then return inline('\\\\{}\n') end end },
  { Div = Div, Figure = Figure, Table = Table, Blocks = Blocks },
}
