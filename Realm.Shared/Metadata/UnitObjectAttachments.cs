using Realm.Shared.Animation;

namespace Realm.Shared.Metadata;

public class UnitObjectAttachments
{
	public List<Dictionary<string, HandAttachmentOrientation>>? right_hand { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? left_hand { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? chest { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? root { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? head { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? left_foot { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? right_foot { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? ground { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? center { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? overhead { get; set; }
	public List<Dictionary<string, HandAttachmentOrientation>>? pivot { get; set; }

	public bool HasAny() => HasAnyUpper() || HasAnyLower() || HasAnyMisc();

	private bool HasAnyUpper() => (right_hand?.Count > 0) || (left_hand?.Count > 0) || (chest?.Count > 0) || (head?.Count > 0);
	private bool HasAnyLower() => (root?.Count > 0) || (left_foot?.Count > 0) || (right_foot?.Count > 0) || (ground?.Count > 0);
	private bool HasAnyMisc() => (center?.Count > 0) || (overhead?.Count > 0) || (pivot?.Count > 0);

	public List<Dictionary<string, HandAttachmentOrientation>>? GetSocketList(string socket)
	{
		if (string.IsNullOrEmpty(socket)) return right_hand;
		string s = socket.ToLowerInvariant().Replace("_", "").Replace(" ", "");
		var upper = GetUpperSocketList(s);
		if (upper != null) return upper;
		var lower = GetLowerSocketList(s);
		if (lower != null) return lower;
		return GetMiscSocketList(s) ?? right_hand;
	}

	private List<Dictionary<string, HandAttachmentOrientation>>? GetUpperSocketList(string s) => s switch
	{
		"chest" or "spine" => chest,
		"head" => head,
		"lefthand" => left_hand,
		"righthand" => right_hand,
		_ => null
	};

	private List<Dictionary<string, HandAttachmentOrientation>>? GetLowerSocketList(string s) => s switch
	{
		"root" or "hips" => root,
		"leftfoot" => left_foot,
		"rightfoot" => right_foot,
		"ground" or "footprint" or "base" => ground,
		_ => null
	};

	private List<Dictionary<string, HandAttachmentOrientation>>? GetMiscSocketList(string s) => s switch
	{
		"center" or "centerofmass" => center,
		"overhead" or "top" or "crown" or "roof" => overhead,
		"pivot" or "origin" => pivot,
		_ => null
	};

	public void SetSocketList(string socket, List<Dictionary<string, HandAttachmentOrientation>> list)
	{
		string s = (socket ?? "righthand").ToLowerInvariant().Replace("_", "").Replace(" ", "");
		if (TrySetUpperSocketList(s, list)) return;
		if (TrySetLowerSocketList(s, list)) return;
		if (TrySetMiscSocketList(s, list)) return;
		right_hand = list;
	}

	private bool TrySetUpperSocketList(string s, List<Dictionary<string, HandAttachmentOrientation>> list)
	{
		switch (s)
		{
			case "chest": case "spine": chest = list; return true;
			case "head": head = list; return true;
			case "lefthand": left_hand = list; return true;
			case "righthand": right_hand = list; return true;
		}
		return false;
	}

	private bool TrySetLowerSocketList(string s, List<Dictionary<string, HandAttachmentOrientation>> list)
	{
		switch (s)
		{
			case "root": case "hips": root = list; return true;
			case "leftfoot": left_foot = list; return true;
			case "rightfoot": right_foot = list; return true;
			case "ground": case "footprint": case "base": ground = list; return true;
		}
		return false;
	}

	private bool TrySetMiscSocketList(string s, List<Dictionary<string, HandAttachmentOrientation>> list)
	{
		switch (s)
		{
			case "center": case "centerofmass": center = list; return true;
			case "overhead": case "top": case "crown": case "roof": overhead = list; return true;
			case "pivot": case "origin": pivot = list; return true;
		}
		return false;
	}

	public List<Dictionary<string, HandAttachmentOrientation>>? GetBoneList(HumanoidBone bone)
	{
		return bone switch
		{
			HumanoidBone.LeftHand => left_hand,
			HumanoidBone.RightHand => right_hand,
			HumanoidBone.Chest or HumanoidBone.Spine => chest,
			HumanoidBone.Hips => root,
			HumanoidBone.Head => head,
			HumanoidBone.LeftFoot => left_foot,
			HumanoidBone.RightFoot => right_foot,
			_ => right_hand
		};
	}

	public void SetBoneList(HumanoidBone bone, List<Dictionary<string, HandAttachmentOrientation>> list)
	{
		switch (bone)
		{
			case HumanoidBone.LeftHand: left_hand = list; break;
			case HumanoidBone.RightHand: right_hand = list; break;
			case HumanoidBone.Chest:
			case HumanoidBone.Spine: chest = list; break;
			case HumanoidBone.Hips: root = list; break;
			case HumanoidBone.Head: head = list; break;
			case HumanoidBone.LeftFoot: left_foot = list; break;
			case HumanoidBone.RightFoot: right_foot = list; break;
			default: right_hand = list; break;
		}
	}

	public bool TryGetOrientation(HumanoidBone hand, string attachmentId, out HandAttachmentOrientation? orientation)
	{
		var list = GetBoneList(hand);
		if (list != null && !string.IsNullOrEmpty(attachmentId))
		{
			string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: Path.GetFileNameWithoutExtension(attachmentId);
			foreach (var dict in list)
			{
				if (dict != null)
				{
					foreach (var kvp in dict)
					{
						if (kvp.Key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
						    kvp.Key.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						    Path.GetFileNameWithoutExtension(kvp.Key).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							orientation = kvp.Value;
							return true;
						}
					}
				}
			}
		}
		orientation = default;
		return false;
	}

	public void SetOrientation(HumanoidBone hand, string attachmentId, HandAttachmentOrientation orientation)
	{
		string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);
		var list = GetBoneList(hand);
		if (list == null)
		{
			list = new List<Dictionary<string, HandAttachmentOrientation>>();
			SetBoneList(hand, list);
		}
		UpdateList(list, cleanId, orientation);
	}

	public bool TryGetSocketOrientation(string socket, string attachmentId, out HandAttachmentOrientation? orientation)
	{
		var list = GetSocketList(socket);
		if (list != null && !string.IsNullOrEmpty(attachmentId))
		{
			string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: Path.GetFileNameWithoutExtension(attachmentId);
			foreach (var dict in list)
			{
				if (dict != null)
				{
					foreach (var kvp in dict)
					{
						if (kvp.Key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
						    kvp.Key.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						    Path.GetFileNameWithoutExtension(kvp.Key).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
						{
							orientation = kvp.Value;
							return true;
						}
					}
				}
			}
		}
		orientation = default;
		return false;
	}

	public void SetSocketOrientation(string socket, string attachmentId, HandAttachmentOrientation orientation)
	{
		string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);
		var list = GetSocketList(socket);
		if (list == null)
		{
			list = new List<Dictionary<string, HandAttachmentOrientation>>();
			SetSocketList(socket, list);
		}
		UpdateList(list, cleanId, orientation);
	}

	private static void UpdateList(List<Dictionary<string, HandAttachmentOrientation>> list, string attachmentId, HandAttachmentOrientation orientation)
	{
		foreach (var dict in list)
		{
			if (dict != null)
			{
				foreach (var key in dict.Keys.ToList())
				{
					if (key.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
					    Path.GetFileNameWithoutExtension(key).Equals(attachmentId, StringComparison.OrdinalIgnoreCase))
					{
						if (string.Equals(dict[key].ParentAttachmentId, orientation.ParentAttachmentId, StringComparison.OrdinalIgnoreCase))
						{
							dict[key] = orientation;
							return;
						}
					}
				}
			}
		}
		list.Add(new Dictionary<string, HandAttachmentOrientation>(StringComparer.OrdinalIgnoreCase)
		{
			[attachmentId] = orientation
		});
	}

	public bool RemoveSocketAttachment(string socket, string attachmentId, string? parentAttachmentId = null)
	{
		var list = GetSocketList(socket);
		if (list == null || string.IsNullOrEmpty(attachmentId)) return false;
		string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: Path.GetFileNameWithoutExtension(attachmentId);
		bool removed = false;
		for (int i = list.Count - 1; i >= 0; i--)
		{
			var dict = list[i];
			if (dict == null) continue;
			if (ProcessSocketDictionaryRemoval(dict, attachmentId, cleanId, parentAttachmentId))
			{
				removed = true;
			}
			if (dict.Count == 0)
			{
				list.RemoveAt(i);
			}
		}
		return removed;
	}

	private bool ProcessSocketDictionaryRemoval(Dictionary<string, HandAttachmentOrientation> dict, string attachmentId, string cleanId, string? parentAttachmentId)
	{
		var keysToRemove = dict.Keys.Where(k => ShouldRemoveKey(k, dict[k], attachmentId, cleanId, parentAttachmentId)).ToList();
		bool removed = false;
		foreach (var k in keysToRemove)
		{
			dict.Remove(k);
			removed = true;
		}
		return removed;
	}

	private bool ShouldRemoveKey(string k, HandAttachmentOrientation orientation, string attachmentId, string cleanId, string? parentAttachmentId)
	{
		bool keyMatch = IsMatchingKey(k, attachmentId, cleanId);
		if (!string.IsNullOrEmpty(parentAttachmentId))
		{
			return keyMatch && string.Equals(orientation.ParentAttachmentId, parentAttachmentId, StringComparison.OrdinalIgnoreCase);
		}
		return (keyMatch && string.IsNullOrEmpty(orientation.ParentAttachmentId)) || IsChildOfTarget(orientation.ParentAttachmentId, attachmentId, cleanId);
	}

	private bool IsMatchingKey(string k, string attachmentId, string cleanId)
	{
		return k.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
		       k.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
		       Path.GetFileNameWithoutExtension(k).Equals(cleanId, StringComparison.OrdinalIgnoreCase);
	}

	private bool IsChildOfTarget(string? orientationParentAttachmentId, string attachmentId, string cleanId)
	{
		if (orientationParentAttachmentId == null) return false;
		return orientationParentAttachmentId.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
		       orientationParentAttachmentId.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
		       Path.GetFileNameWithoutExtension(orientationParentAttachmentId).Equals(cleanId, StringComparison.OrdinalIgnoreCase);
	}

	public UnitObjectAttachments Clone()
	{
		static List<Dictionary<string, HandAttachmentOrientation>>? CloneList(List<Dictionary<string, HandAttachmentOrientation>>? src)
		{
			if (src == null) return null;
			var res = new List<Dictionary<string, HandAttachmentOrientation>>(src.Count);
			foreach (var dict in src)
			{
				if (dict == null) continue;
				var d = new Dictionary<string, HandAttachmentOrientation>(dict.Count, StringComparer.OrdinalIgnoreCase);
				foreach (var kvp in dict)
				{
					d[kvp.Key] = kvp.Value.Clone();
				}
				res.Add(d);
			}
			return res;
		}

		return new UnitObjectAttachments
		{
			right_hand = CloneList(right_hand),
			left_hand = CloneList(left_hand),
			chest = CloneList(chest),
			root = CloneList(root),
			head = CloneList(head),
			left_foot = CloneList(left_foot),
			right_foot = CloneList(right_foot),
			ground = CloneList(ground),
			center = CloneList(center),
			overhead = CloneList(overhead),
			pivot = CloneList(pivot)
		};
	}
}