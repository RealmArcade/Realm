import sys
with open('Realm.Shared/GlbPlayerColorProcessor.cs', 'r') as f:
    lines = f.readlines()
    for i, line in enumerate(lines):
        if "PopulateChromaBins" in line:
            print(i + 1, ":", line)
