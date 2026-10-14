with open('Realm.Shared/GlbPlayerColorProcessor.cs', 'r') as f:
    lines = f.readlines()

for i, line in enumerate(lines):
    if "ConvertRgbToOklab" in line:
        print("Line", i + 1, ":", line.strip())

