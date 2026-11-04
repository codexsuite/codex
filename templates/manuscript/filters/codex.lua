-- Manuscript: Codex directives in standard manuscript form. Figures become a bracketed placeholder line, as a
-- submission names its art rather than placing it.
local latex = FORMAT:match('latex') ~= nil

local function raw(text) return pandoc.RawBlock('latex', text) end

local function Div(div)
  if not latex then return nil end
  local c = div.classes
  if c:includes('pagebreak') then return raw('\\clearpage') end
  if c:includes('scene-break') then return raw('\\begin{center}\\#\\end{center}') end
  if c:includes('missing-figure') then
    return pandoc.Para({ pandoc.Str('[Figure:'), pandoc.Space() } .. pandoc.utils.blocks_to_inlines(div.content) .. { pandoc.Str(']') })
  end
  if c:includes('readaloud') or c:includes('sidebar') or c:includes('aside') or c:includes('spell') or c:includes('statblock') then
    local blocks = {}
    if div.attributes['title'] then table.insert(blocks, pandoc.Para({ pandoc.Strong(pandoc.Inlines(div.attributes['title'])) })) end
    for _, b in ipairs(div.content) do
      if not (b.t == 'Div' and b.classes:includes('entry-heading')) then table.insert(blocks, b) end
    end
    return pandoc.BlockQuote(blocks)
  end
  return nil
end

local function Figure(fig)
  if not latex then return nil end
  local caption = pandoc.utils.blocks_to_inlines(fig.caption.long)
  return pandoc.Para({ pandoc.Str('[Figure:'), pandoc.Space() } .. caption .. { pandoc.Str(']') })
end

local function Meta(meta)
  if pandoc.utils.stringify(meta['font'] or '') == 'times' then meta['font-times'] = true end
  return meta
end

-- ulem's \sout (the wrapper's \st) cannot hold a footnote, so notes inside struck text follow the struck text.
local function Strikeout(strike)
  local notes = pandoc.List()
  local walked = strike:walk({ Note = function(note) notes:insert(note); return {} end })
  if #notes == 0 then return nil end
  return pandoc.List({ walked }) .. notes
end

return { { Meta = Meta }, { Strikeout = Strikeout }, { Div = Div, Figure = Figure } }
