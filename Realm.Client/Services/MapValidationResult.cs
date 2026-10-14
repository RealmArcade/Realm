using System.Collections.Generic;

namespace Realm.Client.Services;

public class MapValidationResult
{
	public bool IsValid => Errors.Count == 0;
	public List<string> Errors { get; } = new();
	public List<string> Warnings { get; } = new();
}