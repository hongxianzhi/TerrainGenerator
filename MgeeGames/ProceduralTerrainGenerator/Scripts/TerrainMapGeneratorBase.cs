using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEditor;

[ExecuteInEditMode]
[RequireComponent(typeof(HeightMapGenerator))]
public abstract class TerrainMapGeneratorBase : MonoBehaviour
{
    public const string VERSION = "1.4";

    [Header("Generator Settings")]
    [Space(10)]
    public int seed;
    public Vector2 centerPosition = new Vector2(0, 0);
    [Range(1, 10)]
    public int chunkGridWidth = 1;
    [Range(0, 6)]
    public int levelOfDetail;
    public GameObject viewer;
    public float chunkViewRange;
    public float objectViewRange;

    [Header("Height Map Settings")]
    [Space(10)]
    public float averageMapDepth;
    public List<HeightMapSettings> heightMapSettingsList;

    protected HeightMapGenerator heightMapGenerator;

    protected Queue<HeightMapThreadInfo> heightMapDataThreadInfoQueue = new Queue<HeightMapThreadInfo>();

    public virtual void Start() {

    }

    public virtual void Update() {
        // Process height map data
        if (heightMapDataThreadInfoQueue.Count > 0) {
            for (int i = 0; i < heightMapDataThreadInfoQueue.Count; i++) {
                HeightMapThreadInfo info = heightMapDataThreadInfoQueue.Dequeue();
                ProcessHeightMapData(info);
            }
        }
    }

    public virtual void OnValidate() {
        // Get components
        if (heightMapGenerator == null) {
            heightMapGenerator = GetComponent<HeightMapGenerator>();
        }

        // Round chunk grid width to nearest odd number >= 1
        if (chunkGridWidth % 2 == 0) {
            chunkGridWidth = (int)Mathf.Round(chunkGridWidth / 2) * 2 + 1;
        }

        // Update component settings
        heightMapGenerator.averageMapDepth = averageMapDepth;
        heightMapGenerator.heightMapSettingsList = heightMapSettingsList;
    }

    public virtual void Generate() {
        
    }

    public virtual void Clear() {

    }

    public virtual void ProcessHeightMapData(HeightMapThreadInfo info) {

    }

    public virtual void Randomize() {
        seed = UnityEngine.Random.Range(0, 1000);

        heightMapSettingsList.Clear();

        int numLayers = UnityEngine.Random.Range(1, 4);
        for (int i = 0; i < numLayers; i++) {
            HeightMapSettings settings = new HeightMapSettings();
            settings.Randomize();

            heightMapSettingsList.Add(settings);
        }
    }

    protected void OnHeightMapDataReceived(Vector2 position, float[,] heightMap) {
        lock (heightMapDataThreadInfoQueue) {
            heightMapDataThreadInfoQueue.Enqueue(new HeightMapThreadInfo(position, heightMap));
        }
    }

    private void OnEnable() {
        EditorApplication.update += Update;
    }

    private void OnDisable() {
        EditorApplication.update -= Update;
    }
}