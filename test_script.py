with open('Realm.Shared/Metadata/RealmContainerFile.cs', 'r') as f:
    print(f.read().find("public static byte[] PackItems"))
