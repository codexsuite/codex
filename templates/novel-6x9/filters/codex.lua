-- Novel (6 x 9): Codex directives (docs/prose-format.md, issue #13) for a single-column book block.
local latex = FORMAT:match('latex') ~= nil
local dropcaps = false

local function raw(text) return pandoc.RawBlock('latex', text) end
local function tex(inlines)
  local text = pandoc.write(pandoc.Pandoc({ pandoc.Plain(inlines) }), 'latex'):gsub('\n$', '')
  return text
end
local function esc(text) return tex({ pandoc.Str(text or '') }) end
local function body(blocks)
  local text = pandoc.write(pandoc.Pandoc(blocks), 'latex')
  return text
end

local function Div(div)
  if not latex then return nil end
  local c = div.classes
  if c:includes('readaloud') then return pandoc.BlockQuote(div.content) end
  if c:includes('sidebar') or c:includes('aside') or c:includes('statblock') or c:includes('spell') then
    return raw('\\CodexBox{' .. tex(pandoc.Inlines(div.attributes['title'] or '')) .. '}{' .. body(div.content) .. '}')
  end
  if c:includes('pagebreak') then return raw('\\clearpage') end
  if c:includes('scene-break') then return raw('\\fancybreak{* \\quad * \\quad *}') end
  if c:includes('missing-figure') then
    return raw('\\CodexMissingFigure{' .. tex(pandoc.utils.blocks_to_inlines(div.content)) .. '}')
  end
  return nil
end

local function Blocks(blocks)
  if not latex or not dropcaps then return nil end
  local out, pending = {}, false
  for _, b in ipairs(blocks) do
    if b.t == 'Header' then
      table.insert(out, b)
      pending = b.level == 2
    elseif pending and b.t == 'Para' and b.content[1] and b.content[1].t == 'Str' and utf8.len(b.content[1].text) > 1
      and b.content[1].text:match('^%w') then -- the initials fonts have letters and digits only
      local word = b.content[1].text
      local split = utf8.offset(word, 2)
      local inlines = { pandoc.RawInline('latex', '\\lettrine{' .. esc(word:sub(1, split - 1)) .. '}{' .. esc(word:sub(split)) .. '}') }
      for i = 2, #b.content do table.insert(inlines, b.content[i]) end
      table.insert(out, pandoc.Para(inlines))
      pending = false
    else
      table.insert(out, b)
      pending = false
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
  { Meta = function(meta) if meta['drop-caps'] ~= nil then dropcaps = meta['drop-caps'] end end },
  { Strikeout = Strikeout },
  { Div = Div, Blocks = Blocks },
}
