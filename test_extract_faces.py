import re

with open('Realm.Shared/GlbPlayerColorProcessor.cs', 'r') as f:
    text = f.read()

# I want to find the ExtractFacePositionData to check what it calls
start = text.find('private static List<(Vector3 Pos0, Vector3 Pos1, Vector3 Pos2)> ExtractFacePositionData(')
if start != -1:
    end = text.find('return result;', start)
    print(text[start:end+15])
