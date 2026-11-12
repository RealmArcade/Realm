using System.Numerics;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Genres;
using Realm.Ecs.AI.Policy;

namespace Realm.Ecs.AI;

public class BotController
{
	private IAiGenreProvider _genreProvider = new StandardRtsGenreProvider();
	private IBotDecisionPolicy _decisionPolicy = new LinearUtilityDecisionPolicy();
	private readonly IBotDecisionPolicy _uniformPolicy = new UniformRandomDecisionPolicy();
	private readonly List<GenericAffordance> _affordanceBuffer = new(64);

	private BotProfile _profile = BotProfile.CreateDefault();
	private float[] _weights = Array.Empty<float>();
	private float[] _mapCandidateWeights = Array.Empty<float>();
	private float[] _equalFeatureWeights = Array.Empty<float>();
	private float _decisionInterval = 1.0f;
	private float _aggressionMultiplier = 1.0f;
	private float _actionTemperature = 0.0f;
	private float _mistakeChance = 0.0f;
	private float _timer = 0.0f;
	private int _playerIndex;

	public int PlayerIndex => _playerIndex;
	public float[] Weights => _weights;
	public float[] MapCandidateWeights => _mapCandidateWeights;
	public float DecisionInterval => _decisionInterval;
	public float AggressionMultiplier => _aggressionMultiplier;
	public float ActionTemperature => _actionTemperature;

	public float MistakeChance
	{
		get => _mistakeChance;
		set => _mistakeChance = Math.Clamp(value, 0.0f, 1.0f);
	}
	public BotProfile Profile => _profile;

	public IAiGenreProvider GenreProvider
	{
		get => _genreProvider;
		set => _genreProvider = value ?? new StandardRtsGenreProvider();
	}

	public IBotDecisionPolicy DecisionPolicy
	{
		get => _decisionPolicy;
		set => _decisionPolicy = value ?? new LinearUtilityDecisionPolicy();
	}

	public Action<int, string, Vector3, string>? CustomActionCallback { get; set; }

	/// <summary>
	/// Supplies candidate actions a map registered for this player. Whoever owns the world sets it once, so a seat
	/// keeps receiving them when its genre changes. While the list comes back non-empty it takes the seat's
	/// decision for that tick, because its candidates carry their own feature vector and are scored with
	/// <see cref="MapCandidateWeights"/>; the genre scan resumes once the map clears what it registered.
	/// </summary>
	public Func<List<GenericAffordance>?>? MapCandidateSource { get; set; }

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
		if (!string.IsNullOrWhiteSpace(profile.Genre)
			&& !string.Equals(_genreProvider.GenreName, profile.Genre, StringComparison.OrdinalIgnoreCase))
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
		_mistakeChance = Math.Clamp(profile.MistakeChance, 0.0f, 1.0f);

		int requiredFeatures = _genreProvider.FeatureCount;
		if (profile.Weights != null && profile.Weights.Length >= requiredFeatures)
		{
			_weights = (float[])profile.Weights.Clone();
		}
		else
		{
			_weights = _genreProvider.GetDefaultWeights();
		}

		_mapCandidateWeights = profile.MapCandidateWeights != null && profile.MapCandidateWeights.Length > 0
			? (float[])profile.MapCandidateWeights.Clone()
			: Array.Empty<float>();
	}

	public void Tick(EcsWorld world, int playerIndex, float delta)
	{
		_playerIndex = playerIndex;
		_timer += delta;
		if (_timer < _decisionInterval)
		{
			return;
		}

		_timer = 0.0f;

		List<GenericAffordance>? mapCandidates = MapCandidateSource?.Invoke();
		if (mapCandidates != null && mapCandidates.Count > 0)
		{
			IBotDecisionPolicy candidatePolicy = _mistakeChance > 0.0f && SeededRandom.Shared.NextDouble() < _mistakeChance
				? _uniformPolicy
				: _decisionPolicy;
			GenericAffordance? chosenCandidate = candidatePolicy.SelectAction(
				mapCandidates,
				ResolveMapCandidateWeights(mapCandidates[0].FeatureVector.Length),
				0.0f,
				1.0f,
				SeededRandom.Shared);

			if (chosenCandidate.HasValue)
			{
				ExecuteSelected(world, playerIndex, chosenCandidate.Value);
				return;
			}
		}

		_genreProvider.ScanAffordances(world, playerIndex, _affordanceBuffer);

		IBotDecisionPolicy policy = _mistakeChance > 0.0f && SeededRandom.Shared.NextDouble() < _mistakeChance
			? _uniformPolicy
			: _decisionPolicy;
		var action = policy.SelectAction(_affordanceBuffer, _weights, _actionTemperature, _aggressionMultiplier, SeededRandom.Shared);

		if (!action.HasValue)
		{
			return;
		}

		ExecuteSelected(world, playerIndex, action.Value);
	}

	private void ExecuteSelected(EcsWorld world, int playerIndex, in GenericAffordance selected)
	{
		if (selected.SourceEntity.IsNull && CustomActionCallback != null)
		{
			CustomActionCallback(playerIndex, selected.PayloadId, selected.TargetPosition, selected.Intent.ToString());
			return;
		}

		_genreProvider.ExecuteAction(world, playerIndex, selected, CustomActionCallback);
	}

	private float[] ResolveMapCandidateWeights(int featureCount)
	{
		if (featureCount <= 0) return Array.Empty<float>();
		if (_mapCandidateWeights.Length == featureCount) return _mapCandidateWeights;

		if (_equalFeatureWeights.Length != featureCount)
		{
			_equalFeatureWeights = new float[featureCount];
			Array.Fill(_equalFeatureWeights, 1.0f);
		}

		return _equalFeatureWeights;
	}
}