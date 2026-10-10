namespace Realm.Client.Tests;

using GdUnit4;
using Realm.Client.Animation;
using Realm.Shared.Animation;
using System;
using System.Collections.Generic;
using System.IO;

[TestSuite]
[RequireGodotRuntime]
public class FbxAnimationConversionTests
{
    private const string MixamoDownloadsDirectory = @"C:\temp\backup\MixamoDownloads";

    [TestCase]
    public void ConvertAllMixamoFbxToRanim()
    {
        Assertions.AssertThat(Directory.Exists(MixamoDownloadsDirectory)).IsTrue();

        string[] fbxFiles = Directory.GetFiles(MixamoDownloadsDirectory, "*.fbx", SearchOption.TopDirectoryOnly);
        Assertions.AssertThat(fbxFiles.Length > 0).IsTrue();

        int totalFiles = fbxFiles.Length;
        int successfulConversions = 0;
        int failedConversions = 0;
        List<string> failureDetails = new List<string>();

        foreach (string fbxFilePath in fbxFiles)
        {
            try
            {
                string originalFileName = Path.GetFileNameWithoutExtension(fbxFilePath);
                List<(string AnimationName, RealmAnimationData Data)> extractedAnimations = MixamoAnimationImporter.ExtractAnimationsFromFile(fbxFilePath, originalFileName);

                if (extractedAnimations == null || extractedAnimations.Count == 0)
                {
                    failedConversions++;
                    failureDetails.Add($"{Path.GetFileName(fbxFilePath)}: No animations extracted");
                    continue;
                }

                for (int animationIndex = 0; animationIndex < extractedAnimations.Count; animationIndex++)
                {
                    (string animationName, RealmAnimationData animationData) = extractedAnimations[animationIndex];
                    string outputRanimPath = extractedAnimations.Count == 1
                        ? Path.ChangeExtension(fbxFilePath, ".ranim")
                        : Path.Combine(MixamoDownloadsDirectory, $"{originalFileName}_{animationIndex}.ranim");

                    RealmAnimationSerializer.SaveToFile(outputRanimPath, animationData);

                    Assertions.AssertThat(File.Exists(outputRanimPath)).IsTrue();
                    FileInfo ranimFileInfo = new FileInfo(outputRanimPath);
                    Assertions.AssertThat(ranimFileInfo.Length > 0).IsTrue();

                    RealmAnimationData deserializedAnimationData = RealmAnimationSerializer.LoadFromFile(outputRanimPath);
                    Assertions.AssertThat(deserializedAnimationData).IsNotNull();
                    Assertions.AssertThat(deserializedAnimationData.Tracks.Length > 0).IsTrue();
                }

                successfulConversions++;
            }
            catch (Exception exception)
            {
                failedConversions++;
                failureDetails.Add($"{Path.GetFileName(fbxFilePath)}: {exception.Message}");
            }
        }

        global::Godot.GD.Print($"[FbxAnimationConversionTests] Converted: {successfulConversions}/{totalFiles} FBX files successfully. Failures: {failedConversions}");

        if (failureDetails.Count > 0)
        {
            foreach (string failureDetail in failureDetails)
            {
                global::Godot.GD.PrintErr($"[FbxAnimationConversionTests] Failure: {failureDetail}");
            }
        }

        Assertions.AssertThat(successfulConversions > 0).IsTrue();
        Assertions.AssertThat(failedConversions).IsEqual(0);
    }
}
