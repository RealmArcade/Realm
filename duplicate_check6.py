import sys

def check_file(filename, search_term):
    try:
        with open(filename, 'r') as f:
            lines = f.readlines()
            for i, line in enumerate(lines):
                if search_term in line:
                    print(f"{filename}:{i+1}:{line.strip()}")
    except:
        pass

check_file('Realm.Client/Services/EditorService.cs', 'SpawnDecalExternalWithParams')
