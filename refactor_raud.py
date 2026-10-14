import re

with open('Realm.Shared/Audio/RaudFile.cs', 'r') as f:
    raud_content = f.read()
    print("RaudFile Length:", len(raud_content))

with open('Realm.Shared/Textures/RtexFile.cs', 'r') as f:
    rtex_content = f.read()
    print("RtexFile Length:", len(rtex_content))
