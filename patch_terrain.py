with open('Realm.Client/EditableTerrain.cs', 'r') as f:
    content = f.read()

new_helper = """	private bool TryGetScaledTerrainArrays(int newWidth, int newDepth, out int finalWidth, out int finalDepth, out int oldWidth, out int oldDepth, out TerrainCell[,] oldCells, out int[,] oldPathing, out TerrainSplatWeights[,] oldSplatMap, out TerrainSplatWeights[,] oldCliffSplatMap, out TerrainCell[,] newCells, out int[,] newPathing, out TerrainSplatWeights[,] newSplatMap, out TerrainSplatWeights[,] newCliffSplatMap, out TerrainState state)
	{
		finalWidth = 0; finalDepth = 0; oldWidth = 0; oldDepth = 0;
		oldCells = null!; oldPathing = null!; oldSplatMap = null!; oldCliffSplatMap = null!;
		newCells = null!; newPathing = null!; newSplatMap = null!; newCliffSplatMap = null!;
		state = default;

		if (!TryInitializeResizeOrScale(newWidth, newDepth, out finalWidth, out finalDepth, out state)) return false;
		
		oldWidth = state.Width;
		oldDepth = state.Depth;
		oldCells = Cells;
		oldPathing = state.PathingCodes;
		oldSplatMap = SplatMap;
		oldCliffSplatMap = CliffSplatMap;

		InitializeNewTerrainArrays(finalWidth, finalDepth, out newCells, out newPathing, out newSplatMap, out newCliffSplatMap);
		return true;
	}
"""

content = content.replace("	public override void ResizeTerrain(int newWidth, int newDepth)", new_helper + "\n	public override void ResizeTerrain(int newWidth, int newDepth)")

resize_to_replace = """	public override void ResizeTerrain(int newWidth, int newDepth)
	{
		if (!TryInitializeResizeOrScale(newWidth, newDepth, out var clampedWidth, out var clampedDepth, out var stateVal)) return;
		ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);

		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		var oldCells = Cells;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;
		TerrainSplatWeights[,] oldCliffSplatMap = CliffSplatMap;

		newWidth = clampedWidth;
		newDepth = clampedDepth;

			InitializeNewTerrainArrays(newWidth, newDepth, out var newCells, out var newPathing, out var newSplatMap, out var newCliffSplatMap);

		int offsetX = (newWidth - oldWidth) / 2;
		int offsetZ = (newDepth - oldDepth) / 2;"""

resize_replacement = """	public override void ResizeTerrain(int newWidth, int newDepth)
	{
		if (!TryGetScaledTerrainArrays(newWidth, newDepth, out newWidth, out newDepth, out var oldWidth, out var oldDepth, out var oldCells, out var oldPathing, out var oldSplatMap, out var oldCliffSplatMap, out var newCells, out var newPathing, out var newSplatMap, out var newCliffSplatMap, out var state)) return;

		int offsetX = (newWidth - oldWidth) / 2;
		int offsetZ = (newDepth - oldDepth) / 2;"""

content = content.replace(resize_to_replace, resize_replacement)

scale_to_replace = """	public override void ScaleTerrainData(int newWidth, int newDepth)
	{
		if (!TryInitializeResizeOrScale(newWidth, newDepth, out var clampedWidth, out var clampedDepth, out var stateVal)) return;
		ref var state = ref GameContext.World.Get<TerrainState>(ECSContext.WorldEntity);

		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		var oldCells = Cells;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;
		TerrainSplatWeights[,] oldCliffSplatMap = CliffSplatMap;

		newWidth = clampedWidth;
		newDepth = clampedDepth;

			InitializeNewTerrainArrays(newWidth, newDepth, out var newCells, out var newPathing, out var newSplatMap, out var newCliffSplatMap);

		float[,] newGridHeights = CalculateScaledGridHeights(newWidth, newDepth, oldWidth, oldDepth, oldCells);"""

scale_replacement = """	public override void ScaleTerrainData(int newWidth, int newDepth)
	{
		if (!TryGetScaledTerrainArrays(newWidth, newDepth, out newWidth, out newDepth, out var oldWidth, out var oldDepth, out var oldCells, out var oldPathing, out var oldSplatMap, out var oldCliffSplatMap, out var newCells, out var newPathing, out var newSplatMap, out var newCliffSplatMap, out var state)) return;

		float[,] newGridHeights = CalculateScaledGridHeights(newWidth, newDepth, oldWidth, oldDepth, oldCells);"""

content = content.replace(scale_to_replace, scale_replacement)

with open('Realm.Client/EditableTerrain.cs', 'w') as f:
    f.write(content)
