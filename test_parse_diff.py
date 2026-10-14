import sys
import re

def process_file():
    with open('Realm.AdminServer/Program.cs', 'r') as f:
        lines = f.readlines()
        print(f"Total lines: {len(lines)}")
        
if __name__ == '__main__':
    process_file()
