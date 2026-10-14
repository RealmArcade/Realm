with open('Realm.Shared/GlbPlayerColorProcessor.cs', 'r') as f:
    lines = f.readlines()

def print_lines(start, end):
    for i in range(start, end):
        if i < len(lines):
            print(f"{i+1}: {lines[i].rstrip()}")

print_lines(194, 210)
print("\n--- AND ---\n")
print_lines(343, 360)
