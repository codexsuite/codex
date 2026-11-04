pandoc  -f markdown+smart+raw_tex -d 'PlayersGuide.yaml' -t latex --filter=../../lib/dndtable_filter.py
#latexmk -pdf 'PlayersGuide.tex'

# find . -name "*.webp" -print0 | parallel -0 dwebp {} -o {.}.png
# parallel dwebp {} -o {.}.png ::: *.webp

