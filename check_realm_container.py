import sys
import re

def main():
    with open('Realm.Shared/Metadata/RealmContainerFile.cs', 'r') as f:
        content = f.read()

    print(content[:500])

if __name__ == '__main__':
    main()
