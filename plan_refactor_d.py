with open('Realm.Shared/GlbPlayerColorProcessor.cs', 'r') as f:
    content = f.read()

import re
matches = re.finditer(r'var pixel = albedoImg\.GetPixel\(x, y\);\s+if \(pixel\.Alpha < 128\) continue;\s+float r = pixel\.Red / 255f;\s+float g = pixel\.Green / 255f;\s+float b = pixel\.Blue / 255f;\s+var \(pL, pA, pB\) = ConvertRgbToOklab\(r, g, b\);', content)
for m in matches:
    print(m.start())
