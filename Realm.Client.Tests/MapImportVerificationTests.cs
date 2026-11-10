using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdUnit4;
using Godot;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.UI.MapEditor;
using Realm.Shared.Distribution;
using Environment = System.Environment;

namespace Realm.Client.Tests;

[TestSuite]
public partial class MapImportVerificationTests
	{
	private const BindingFlags PrivateInstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
	private const string SourceTitle = "Ancient Realm";
	private const string SourceVersion = "1.0.0";
	private const string SourceAttribution = "Ancient Realm v1.0.0";
}
