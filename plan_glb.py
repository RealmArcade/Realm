with open('Realm.Shared/GlbPlayerColorProcessor.cs', 'r') as f:
    content = f.read()

import re
matches = re.finditer(r'var \(pL, pA, pB\) = ConvertRgbToOklab\(r, g, b\);', content)
for match in matches:
    print(match.start(), match.group())

