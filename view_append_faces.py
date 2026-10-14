with open('Realm.Shared/GlbPlayerColorProcessor.cs', 'r') as f:
    content = f.read()

print("File len:", len(content))
lines = content.splitlines()

# Search for "AppendIndexedVec2Faces"
for i, line in enumerate(lines):
    if "AppendIndexedVec2Faces" in line:
        print(i + 1, ":", line)
        for j in range(i + 1, i + 15):
            print(lines[j])
        print("===")
