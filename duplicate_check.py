import sys

def check_file(filename, search_term):
    with open(filename, 'r') as f:
        lines = f.readlines()
        for i, line in enumerate(lines):
            if search_term in line:
                print(f"{filename}:{i+1}:{line.strip()}")

check_file('Realm.Client/Network/EnetMapTransferService.cs', 'ReceiveMapManifestRpc')
check_file('Realm.Client/Network/LobbyManager.cs', 'ReceiveMapManifestRpc')
