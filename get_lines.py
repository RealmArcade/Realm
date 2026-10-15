import sys

with open('Realm.Client/Services/NetworkService.cs', 'r') as f:
    lines = f.readlines()

for i, l in enumerate(lines):
    if 'ApplySnapshotToUnit' in l:
        print(''.join(lines[i-1:i+10]))
