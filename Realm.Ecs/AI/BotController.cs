using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;
using System.Numerics;

namespace Realm.Ecs.AI;

public class BotController
{
	private IAiGenreProvider _genreProvider = new StandardRtsGenreProvider();
	private readonly LinearUtilityPolicy _policy = new();
	private readonly List<GenericAffordance> _affordanceBuffer = new(64);

	private BotProfile _profile = BotProfile.CreateDefault();
	private float[] _weights = Array.Empty<float>();
	private float _decisionInterval = 1.0f;
	private float _aggressionMultiplier = 1.0f;
	private float _actionTemperature = 0.0f;
	private float _timer = 0.0f;
	private int _playerIndex;

	public int PlayerIndex => _playerIndex;
	public float[] Weights => _weights;
	public float DecisionInterval => _decisionInterval;
	public float AggressionMultiplier => _aggressionMultiplier;
	public float ActionTemperature => _actionTemperature;
	public BotProfile Profile => _profile;
	public IAiGenreProvider GenreProvider
	{
		get => _genreProvider;
		set => _genreProvider = value ?? new StandardRtsGenreProvider();
	}

	public Action<int, string, Vector3, string>? CustomActionCallback { get; set; }

	public BotController(int playerIndex = 0, BotProfile? profile = null, IAiGenreProvider? genreProvider = null)
	{
		_playerIndex = playerIndex;
		if (genreProvider != null)
		{
			_genreProvider = genreProvider;
		}
		LoadProfile(profile ?? BotProfile.CreateDefault("GenericMap", _genreProvider.GenreName));
	}

	public void LoadProfile(BotProfile profile)
	{
		if (profile == null)
		{
			return;
		}

		_profile = profile;
		if (!string.IsNullOrWhiteSpace(profile.Genre))
		{
			_genreProvider = AiGenreRegistry.Get(profile.Genre);
		}

		if (profile.CustomParameters != null && profile.CustomParameters.Count > 0)
		{
			_genreProvider.ConfigureFromParameters(profile.CustomParameters);
		}

		_decisionInterval = profile.DecisionIntervalSeconds > 0.05f ? profile.DecisionIntervalSeconds : 1.0f;
		_aggressionMultiplier = profile.AggressionMultiplier > 0.0f ? profile.AggressionMultiplier : 1.0f;
		_actionTemperature = Math.Clamp(profile.ActionTemperature, 0.0f, 2.0f);

		int requiredFeatures = _genreProvider.FeatureCount;
		if (profile.Weights != null && profile.Weights.Length >= requiredFeatures)
		{
			_weights = (float[])profile.Weights.Clone();
		}
		else
		{
			_weights = _genreProvider.GetDefaultWeights();
		}
	}

	public void Tick(World world, int playerIndex, float delta)
	{
		_playerIndex = playerIndex;
		_timer += delta;
		if (_timer < _decisionInterval)
		{
			return;
		}
		_timer = 0.0f;

		_genreProvider.ScanAffordances(world, playerIndex, _affordanceBuffer);
		var action = _policy.SelectAction(_affordanceBuffer, _weights, 0.0f, _actionTemperature, _aggressionMultiplier);

		if (action.HasValue)
		{
			_genreProvider.ExecuteAction(world, playerIndex, action.Value, CustomActionCallback);
		}
	}
}
