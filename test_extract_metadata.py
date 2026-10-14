def replace_methods():
    files = ['Realm.Shared/Audio/RaudFile.cs', 'Realm.Shared/Textures/RtexFile.cs']
    for file in files:
        with open(file, 'r') as f:
            print(f.read().find("public static string? ExtractMetadata("))

replace_methods()
