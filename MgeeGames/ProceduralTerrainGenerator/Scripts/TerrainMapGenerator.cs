using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

[ExecuteInEditMode]
public class TerrainMapGenerator : MonoBehaviour {
    public const string VERSION = "1.3";

    [Header("Generator Settings")]
    [Space(10)]
    public int seed;
    public Vector2 centerPosition = new Vector2(0, 0);
    [Range (1, 10)]
    public int chunkGridWidth = 1;
    [Range (0, 6)]
    public int levelOfDetail;
    public GameObject viewer;
    public float chunkViewRange;
    public float objectViewRange;

    [Header("Terrain Settings")]
    [Space(10)]
    public Gradient terrainColourGradient;
    public Material terrainMaterial;
    public bool createForest;
    public bool createWater;

    [Header("Height Map Settings")]
    [Space(10)]
    public float averageMapDepth;
    public List<HeightMapSettings> heightMapSettingsList;

    [Header("Hydraulic Erosion Settings")]
    [Space(10)]
    public HydraulicErosionSettings hydraulicErosionSettings;

    [Header("Forest Settings")]
    [Space(10)]
    public ForestGeneratorSettings forestGeneratorSettings;

    [Header("Water Settings")]
    [Space(10)]
    public Material waterMaterial;
    public float waterLevel;
    public float waveSpeed;
    public float waveStrength;

    const int chunkWidth = 241;

    HeightMapGenerator heightMapGenerator;
    ForestGenerator forestGenerator;
    HydraulicErosion hydraulicErosion;

    Dictionary<Vector2, GameObject> terrainChunks = new Dictionary<Vector2, GameObject>();

    Queue<HeightMapThreadInfo> heightMapDataThreadInfoQueue = new Queue<HeightMapThreadInfo>();
    Queue<MeshDataThreadInfo> meshDataThreadInfoQueue = new Queue<MeshDataThreadInfo>();

    Dictionary<int, Dictionary<string, int>> IDmap = new Dictionary<int, Dictionary<string, int>>();

#if UNITY_EDITOR
    void OnEnable() {
        EditorApplication.update += Update;
    }

    void OnDisable() {
        EditorApplication.update -= Update;
    }
#endif

    Dictionary<int, int> IDConvert = new Dictionary<int, int>();
    int TransformID(int id)
    {
        if (IDConvert.TryGetValue(id, out int result))
        {
            return result;
        }
        return id;
    }

    int IDMapSize
    {
        get
        {
            return IDmap.Count;
        }
    }

    class TileSet
    {
        public int firstgid;
        public int tilecount;
        public string source;
    }

    void UpdateTileSetMap(JArray array)
    {
        IDConvert.Clear();
        List<TileSet> list = new List<TileSet>();
        foreach (var item in array)
        {
            string source = item["source"].ToString();
            source = Path.GetFileNameWithoutExtension(source);
            int firstgid = int.Parse(item["firstgid"].ToString());
            list.Add(new TileSet { firstgid = firstgid, source = source });
        }
        list.Sort((a, b) => a.firstgid - b.firstgid);
        Dictionary<string,TileSet> tileset = new Dictionary<string, TileSet>();
        for(int i = 0; i < list.Count; ++i)
        {
            var item = list[i];
            if(i + 1 < list.Count)
            {
                item.tilecount = list[i + 1].firstgid - item.firstgid;
            }
            else
            {
                item.tilecount = int.MaxValue;
            }
            tileset.Add(item.source, item);
        }

        var iter = IDmap.GetEnumerator();
        while(iter.MoveNext())
        {
            bool found = false;
            int id = iter.Current.Key;
            var setMap = iter.Current.Value;
            var setIter = setMap.GetEnumerator();
            while(setIter.MoveNext())
            {
                string name = setIter.Current.Key;
                int itemID = setIter.Current.Value;
                if(tileset.TryGetValue(name, out TileSet tile) && tile.tilecount > itemID)
                {
                    found = true;
                    IDConvert[tile.firstgid + itemID] = id;
                    break;
                }
            }

            if(!found)
            {
                Debug.LogWarning("Can't find tileset for id " + id);
            }
        }
    }

    void InitIDMap(string text)
    {
        IDmap.Clear();
        var dict = JObject.Parse(text) as JObject;
        if (dict == null)
        {
            return;
        }
        
        foreach(var item in dict)
        {
            int id = int.Parse(item.Key);
            JObject sets = item.Value as JObject;
            //遍历sets
            Dictionary<string, int> setMap = new Dictionary<string, int>();
            foreach (var set in sets)
            {
                string key = set.Key;
                setMap[key] = int.Parse(set.Value.ToString());
            }
            IDmap[id] = setMap;
        }
    }

    void LoadTileMap(string path, out HashSet<long> paths, out int mapWidth, out int mapHeight)
    {
        mapWidth = 0;
        mapHeight = 0;
        paths = new HashSet<long>();
        if(string.IsNullOrEmpty(path))
        {
            return;
        }
        JObject tilemap = JObject.Parse(File.ReadAllText(path));
        UpdateTileSetMap(tilemap["tilesets"] as JArray);

        mapWidth = (int)(tilemap["width"]);
		mapHeight = (int)(tilemap["height"]);
        var layerData = tilemap["layers"];
        Dictionary<long, int> canpass_dict = new Dictionary<long, int>();
        foreach (var layer in layerData)
        {
            if((string)(layer["type"]) == "tilelayer")
            {
                int pos = 0;
                var layerName = (string)(layer["name"]);
                var layerDataArray = (JArray)(layer["data"]);
                foreach (var item in layerDataArray)
                {
                    canpass_dict.TryGetValue(pos, out int canPass);
                    int id = TransformID((int)item);
                    if(id == 0)
                    {
                        canPass |= 1;
                    }
                    else if(id >= 101 && id <= 105 || id >= 301 && id <= 303)
                    {
                        canPass |= 2;
                    }
                    else if(id >= 110 && id <= 199)
                    {
                        canPass |= 4;
                    }
                    canpass_dict[pos++] = canPass;
                }
            }
        }

        var iter = canpass_dict.GetEnumerator();
        while(iter.MoveNext())
        {
            long pos = iter.Current.Key;
            int canPass = iter.Current.Value;
            if(canPass == 1 || ((canPass & 4) == 4))
            {
                //不可通行
            }
            else
            {
                paths.Add(pos);
            }
        }
    }

    void Initialize() {
        heightMapGenerator = GetComponent<HeightMapGenerator>();
        forestGenerator = GetComponent<ForestGenerator>();
        hydraulicErosion = GetComponent<HydraulicErosion>();

        foreach (Transform child in transform) {
            Vector2 position = new Vector2(child.position.x / chunkWidth, child.position.z / chunkWidth);
            if (!terrainChunks.ContainsKey(position)) {
                terrainChunks.Add(position, child.gameObject);
            }
        }

        // Initialize Forest Generator
        List<GameObject> trees = new List<GameObject>();
        foreach (KeyValuePair<Vector2, GameObject> chunkEntry in terrainChunks) {
            Transform forestTransform = chunkEntry.Value.transform.Find("Forest");

            if (forestTransform != null) {
                foreach (Transform treeTransform in forestTransform) {
                    trees.Add(treeTransform.gameObject);
                }   
            }
        }

        forestGenerator.Init(trees, viewer, objectViewRange);

        InitIDMap(System.IO.File.ReadAllText("Assets/Resources/map/idmap.json"));
    }

    void OnValidate() {
        // Get components
        if (heightMapGenerator == null) {
            heightMapGenerator = GetComponent<HeightMapGenerator>();
        }

        if (forestGenerator == null) {
            forestGenerator = GetComponent<ForestGenerator>();
        }

        if (hydraulicErosion == null) {
            hydraulicErosion = GetComponent<HydraulicErosion>();
        }

        // Round chunk grid width to nearest odd number >= 1
        if (chunkGridWidth % 2 == 0) {
            chunkGridWidth = (int)Mathf.Round(chunkGridWidth / 2) * 2 + 1;
        }

        // Update component settings
        heightMapGenerator.averageMapDepth = averageMapDepth;
        heightMapGenerator.heightMapSettingsList = heightMapSettingsList;
    
        forestGenerator.settings = forestGeneratorSettings;

        hydraulicErosion.settings = hydraulicErosionSettings;

        if (hydraulicErosion.settings.useHydraulicErosion && chunkGridWidth > 1) {
            Debug.LogWarning("Can only use Hydraulic Erosion for single chunks");
            hydraulicErosion.settings.useHydraulicErosion = false;
        }
    }

    void PostProcessPath(float[,] heightMap, int x, int y, float value, HashSet<long> paths)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);
        if(x < 0 || x >= width || y < 0 || y >= height)
        {
            return;
        }
        int offset = (int)(heightMap[x, y] - value);
        heightMap[x, y] = value;

        //以x,y为中心，向周围扩展offset个单位，采样并设置高度
        int sample = 3;
        offset = Mathf.Min(offset, 4);
        for(int i = -offset; i <= offset; i++)
        {
            for(int j = -offset; j <= offset; j++)
            {
                int nx = x + i;
                int ny = y + j;
                if(nx < 0 || nx >= width || ny < 0 || ny >= height)
                {
                    continue;
                }
                if(paths.Contains(nx * width + ny))
                {
                    continue;
                }
                float h = 0;
                int cnt = 0;
                for(int k = -sample; k <= sample; k++)
                {
                    for(int l = -sample; l <= sample; l++)
                    {
                        int nnx = nx + k;
                        int nny = ny + l;
                        if(nnx < 0 || nnx >= width || nny < 0 || nny >= height)
                        {
                            continue;
                        }
                        h += heightMap[nnx, nny];
                        cnt++;
                    }
                }
                heightMap[nx, ny] = Mathf.Lerp(heightMap[nx, ny], h / cnt, 0.5f);
            }
        }
    }

    void PostProcessHeightMap(HeightMapThreadInfo info)
    {
        HashSet<long> paths = info.paths;
        if(paths == null || paths.Count == 0)
        {
            return;
        }

        float[,] heightMap = info.heightMap;
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);
        var iter = paths.GetEnumerator();
        while(iter.MoveNext())
        {
            long key = iter.Current;
            PostProcessPath(heightMap, (int)(key / width), (int)(key % width), 0, paths);
        }
    }

    void Update() {
        // Process height map data
        if (heightMapDataThreadInfoQueue.Count > 0) {
            for (int i = 0; i < heightMapDataThreadInfoQueue.Count; i++) {
                HeightMapThreadInfo info = heightMapDataThreadInfoQueue.Dequeue();
                PostProcessHeightMap(info);
                MeshGenerator.RequestMeshData(info.position, info.heightMap, levelOfDetail, OnTerrainMeshDataReceived, terrainColourGradient);
            }
        }

        // Process mesh data
        if (meshDataThreadInfoQueue.Count > 0) {
            for (int i = 0; i < meshDataThreadInfoQueue.Count; i++) {
                MeshDataThreadInfo info = meshDataThreadInfoQueue.Dequeue();

                GameObject chunk;
                if (info.type == MeshType.Water) {
                    chunk = CreateWater(info.position, info.meshData);
                }
                else {
                    chunk = CreateTerrainChunk(info.position, info.meshData);

                    if (createForest) {
                        Vector3[] normals = chunk.GetComponent<MeshFilter>().sharedMesh.normals;
                        GameObject forestGameObject = CreateForest(info.heightMap, normals);
                        forestGameObject.transform.parent = terrainChunks[info.position].transform;
                        forestGameObject.transform.localPosition = new Vector3(0f, 0f, forestGameObject.transform.position.z);
                    }
                }

                chunk.transform.parent = terrainChunks[info.position].transform;
                chunk.transform.localPosition = new Vector3(0f, 0f, chunk.transform.position.z);
            }
        }

        if (viewer == null) return;

        Vector2 position = new Vector2(viewer.transform.position.x, viewer.transform.position.z);

        // Only show Terrain Chunks in range of viewer
        foreach (KeyValuePair<Vector2, GameObject> chunkEntry in terrainChunks) {
            GameObject chunk = chunkEntry.Value;
            Vector2 chunkPosition = new Vector2(chunk.transform.position.x, chunk.transform.position.z);

            if ((position - chunkPosition).magnitude < chunkViewRange) {
                chunk.SetActive(true);
            }
            else {
                chunk.SetActive(false);
            }
        }
    }

    public void Generate(bool loadAllObjects=false) {
        // Generate grid of chunks
        Initialize();
        LoadTileMap("Assets/Resources/map/maincity_0.json", out HashSet<long> paths, out int mapWidth, out int mapHeight);
        //把地图尺寸放大scale倍
        int scale = 1;
        const int maxSide = 100;
        int sideMax = Mathf.Max(mapWidth, mapHeight);
        if(sideMax > maxSide)
        {
            scale = 1;
        }
        else
        {
            scale = maxSide / sideMax;
        }

        HashSet<long> newPaths = paths;
        int newWidth = mapWidth * scale;
        int newHeight = mapHeight * scale;
        if(scale > 1)
        {
            newPaths = new HashSet<long>();
            foreach(var path in paths)
            {
                long pos = path;
                int x = (int)(pos / mapWidth);
                int y = (int)(pos % mapWidth);
                int newx = x * scale;
                int newy = y * scale;
                for(int i = 0; i < scale; i++)
                {
                    for(int j = 0; j < scale; j++)
                    {
                        newPaths.Add((newx + i) * newWidth + newy + j);
                    }
                }
            }
        }
        CreateChunkGrid(newWidth, newHeight, newPaths);
    }

    public void Clear() {
        // Make all chunks visible before clearing
        foreach (KeyValuePair<Vector2, GameObject> chunkEntry in terrainChunks) {
            GameObject chunk = chunkEntry.Value;
            chunk.SetActive(true);
        }

        Transform[] chunkTransforms = GetComponentsInChildren<Transform>();
        GameObject[] chunks = new GameObject[chunkTransforms.Length - 1];
        terrainChunks.Clear();

        int index = 0;
        foreach (Transform chunk in chunkTransforms) {
            if (chunk != transform) {
                chunks[index] = chunk.gameObject;
                index++;
            }
        }

        foreach (GameObject chunk in chunks) {
            DestroyImmediate(chunk, true);
        }

        if (forestGenerator != null) {
            forestGenerator.Clear();
        }
    }

    public void Randomize() {
        seed = UnityEngine.Random.Range(0, 1000);
        waterLevel = UnityEngine.Random.Range(0, 30);

        heightMapSettingsList.Clear();

        int numLayers = UnityEngine.Random.Range(1, 4);
        for (int i = 0; i < numLayers; i++) {
            HeightMapSettings settings = new HeightMapSettings();
            settings.Randomize();

            heightMapSettingsList.Add(settings);
        }
    }

    void CreateChunkGrid(int mapWidth, int mapHeight, HashSet<long> paths) {
        int w = (int)Mathf.Round(chunkGridWidth / 2);
        for (int x = -w; x <= w; x++) {
            for (int y = -w; y <= w; y++) {
                Vector2 pos = new Vector2(centerPosition.x + x, centerPosition.y + y);
                GameObject chunk = new GameObject("TerrainChunk");

                chunk.isStatic = true;
                chunk.transform.parent = transform;
                chunk.transform.position = new Vector3(pos.x * (mapWidth - 1), 0f, -pos.y * (mapHeight - 1));

                if (terrainChunks.ContainsKey(pos)) {
                    DestroyImmediate(terrainChunks[pos], true);
                    terrainChunks[pos] = chunk;
                }
                else {
                    terrainChunks.Add(pos, chunk);
                }

                RequestTerrainChunk(pos, mapWidth, mapHeight, paths);
            }
        }
    }

    void RequestTerrainChunk(Vector2 position, int mapWidth, int mapHeight, HashSet<long> paths) {
        heightMapGenerator.RequestHeightMapData(seed, mapWidth, mapHeight, position, paths, OnHeightMapDataReceived);

        if (createWater) {
            float[,] heightMap = new float[mapWidth, mapHeight];

            for (int z = 0; z < mapHeight; z++) {
                for (int x = 0; x < mapWidth; x++) {
                    heightMap[x, z] = waterLevel;
                }
            }

            MeshGenerator.RequestMeshData(position, heightMap, levelOfDetail, OnWaterMeshDataReceived);
        }
    }

    GameObject CreateForest(float[,] heightMap, Vector3[] terrainNormals) {
        forestGenerator.Clear();
        GameObject forestGameObject = forestGenerator.Generate(heightMap, terrainNormals, waterLevel, seed);

        forestGameObject.isStatic = true;
    
        return forestGameObject;
    }

    void OnHeightMapDataReceived(Vector2 position, float[,] heightMap, HashSet<long> paths) {
        lock (heightMapDataThreadInfoQueue) {
            heightMapDataThreadInfoQueue.Enqueue(new HeightMapThreadInfo(position, heightMap, paths));
        }
    }

    void OnTerrainMeshDataReceived(Vector2 position, float[,] heightMap, MeshData meshData) {
        lock (meshDataThreadInfoQueue) {
            meshDataThreadInfoQueue.Enqueue(new MeshDataThreadInfo(position, heightMap, meshData, MeshType.Terrain));
        }
    }

    void OnWaterMeshDataReceived(Vector2 position, float[,] heightMap, MeshData meshData) {
        lock (meshDataThreadInfoQueue) {
            meshDataThreadInfoQueue.Enqueue(new MeshDataThreadInfo(position, heightMap, meshData, MeshType.Water));
        }
    }

    GameObject CreateTerrainChunk(Vector2 position, MeshData meshData) {
        Mesh mesh = meshData.CreateMesh();

        GameObject terrainGameObject = new GameObject("Terrain");
        terrainGameObject.AddComponent<MeshFilter>();
        terrainGameObject.AddComponent<MeshRenderer>();
        terrainGameObject.AddComponent<MeshCollider>();

        terrainGameObject.GetComponent<MeshRenderer>().material = terrainMaterial;
        terrainGameObject.GetComponent<MeshFilter>().sharedMesh = mesh;
        terrainGameObject.GetComponent<MeshCollider>().sharedMesh = mesh;
    
        terrainGameObject.isStatic = true;

        return terrainGameObject;
    }

    GameObject CreateWater(Vector2 position, MeshData meshData) {
        float offset = position.x * (chunkWidth - 1);

        Mesh mesh = meshData.CreateMesh();

        GameObject waterGameObject = new GameObject("Water");
        waterGameObject.AddComponent<MeshFilter>();
        waterGameObject.AddComponent<MeshRenderer>();
        waterGameObject.AddComponent<WaterManager>();

        waterGameObject.GetComponent<WaterManager>().waterLevel = waterLevel;
        waterGameObject.GetComponent<WaterManager>().waveSpeed = waveSpeed;
        waterGameObject.GetComponent<WaterManager>().waveStrength = waveStrength;
        waterGameObject.GetComponent<WaterManager>().offset = offset;

        waterGameObject.GetComponent<MeshRenderer>().material = waterMaterial;
        waterGameObject.GetComponent<MeshFilter>().sharedMesh = mesh;

        return waterGameObject;
    }
}
