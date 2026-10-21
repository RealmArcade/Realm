with open('Realm.Client/UI/MapEditor/FloatingDialogBase.cs', 'r') as f:
    content = f.read()

content = content.replace("var txtId = filterResult.inputField;", "var txtId = filterResult.Input;")
content = content.replace("var setIdValue = filterResult.setValueAction;", "var setIdValue = filterResult.SetValue;")

with open('Realm.Client/UI/MapEditor/FloatingDialogBase.cs', 'w') as f:
    f.write(content)
