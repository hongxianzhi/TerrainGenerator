﻿using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System;
using UnityEngine;

[System.Serializable]
public class HeightMapSettings {
    public int mapDepth;
    public NoiseType noiseType;
    public float noiseScale;
    public int noiseOctaves;
    public float persistence;
    public float lacunarity;
    public float noiseRedistributionFactor;
    public bool useFalloff;

    public HeightMapSettings() {
        noiseType = NoiseType.Perlin;
    }

    public void Randomize() {
        noiseScale = UnityEngine.Random.Range(1f, 5f);
        noiseOctaves = UnityEngine.Random.Range(1, 6);
        persistence = UnityEngine.Random.Range(0.1f, 0.5f);
        lacunarity = UnityEngine.Random.Range(1f, 2f);
        noiseRedistributionFactor = UnityEngine.Random.Range(1f, 3f);
        mapDepth = UnityEngine.Random.Range(30, 100);
    }
}

public class HeightMapGenerator : MonoBehaviour {
    [HideInInspector]
    public float averageMapDepth;
    [HideInInspector]
    public List<HeightMapSettings> heightMapSettingsList;

    HydraulicErosion hydraulicErosion;

    const int maxWidth = 1024;

    float biomeNoiseScale;
    float biomeDepth;

    Queue<HeightMapThreadInfo> heightMapDataThreadInfoQueue = new Queue<HeightMapThreadInfo>();

    void Start() {
        heightMapSettingsList = new List<HeightMapSettings>();
        heightMapSettingsList.Add(new HeightMapSettings());
    }

    void OnValidate() {
        if (hydraulicErosion == null) {
            hydraulicErosion = GetComponent<HydraulicErosion>();
        }
    }

    public void RequestHeightMapData(int seed, int mapWidth, int mapHeight, Vector2 position, HashSet<long> paths, Action<Vector2, float[,], HashSet<long>> callback) {
        ThreadStart threadStart = delegate {
            HeightMapDataThread(seed, mapWidth, mapHeight, position, paths, callback);
        };

        new Thread(threadStart).Start();
    }

    void HeightMapDataThread(int seed, int mapWidth, int mapHeight, Vector2 position, HashSet<long> paths, Action<Vector2, float[,], HashSet<long>> callback) {
        int mapOffsetX = (int)(position.x * (mapWidth - 1)) + seed;
        int mapOffsetY = (int)(position.y * (mapHeight - 1)) + seed;

        float[,] heightMap = CreateHeightMap(seed, mapWidth, mapHeight, mapOffsetX, mapOffsetY);

        callback(position, heightMap, paths);
    }

    float[,] CreateHeightMap(int seed, int mapWidth, int mapHeight, int offsetX, int offsetY) {
        Noise.CreateNewSimplexNoiseGenerator(seed);

        float[,] heightMap = new float[mapWidth, mapHeight];
        // Initialize height map values
        for (int z = 0; z < mapHeight; z++) {
            for (int x = 0; x < mapWidth; x++) {
                heightMap[x, z] = 0f;
            }
        }

        // Add each height map layer together
        float totalMapDepth = 0f;
        foreach (HeightMapSettings settings in heightMapSettingsList) {
            float[,] layer = CreateHeightMapLayer(settings, seed, mapWidth, mapHeight, offsetX, offsetY);
            totalMapDepth += settings.mapDepth;

            // Determine map depth
            for (int z = 0; z < mapHeight; z++) {
                for (int x = 0; x < mapWidth; x++) {
                    heightMap[x, z] += layer[x, z];
                }
            }
        }

        float actualAverageMapDepth = totalMapDepth / heightMapSettingsList.Count;
        float averageMapDepthDifference = Mathf.Abs(actualAverageMapDepth - averageMapDepth);

        // Adjust map depth to obtain targeted average map depth
        for (int z = 0; z < mapHeight; z++) {
            for (int x = 0; x < mapWidth; x++) {
                heightMap[x, z] -= averageMapDepthDifference;
            }
        }

        return heightMap;
    }

    float[,] CreateHeightMapLayer(HeightMapSettings settings, int seed, int mapWidth, int mapHeight, int offsetX, int offsetY) {
        float widthFactor = (float)mapWidth / (float)mapHeight;
        float[,] noiseMap = Noise.GenerateNoiseMap(
            settings.noiseType,
            mapWidth,
            mapWidth,
            settings.noiseScale * widthFactor,
            offsetX,
            offsetY,
            settings.noiseOctaves,
            settings.persistence,
            settings.lacunarity,
            settings.noiseRedistributionFactor
        );

        float[,] falloffMap = null;
        if (settings.useFalloff) {
            falloffMap = Falloff.GenerateFalloffMap(mapWidth, mapHeight);
        }

        bool useHydraulicErosion = hydraulicErosion.settings.useHydraulicErosion;
        if (useHydraulicErosion && settings.mapDepth > 0) {
            noiseMap = hydraulicErosion.ErodeTerrain(noiseMap, seed);
        }

        float[,] heightMap = new float[mapWidth, mapHeight];

        // Determine map depth
        for (int z = 0; z < mapHeight; z++) {
            for (int x = 0; x < mapWidth; x++) {
                if (settings.useFalloff && settings.mapDepth > 0) {
                    noiseMap[x, z] = Mathf.Clamp01(noiseMap[x, z] - falloffMap[x, z]);
                }

                if (settings.mapDepth == 0) {
                    heightMap[x, z] = 1f;
                }
                else {
                    heightMap[x, z] = 2 * (noiseMap[x, z] * settings.mapDepth);
                }
            }
        }

        return heightMap;
    }
}


public class HeightMapThreadInfo {
    public Vector2 position;
    public float[,] heightMap;
    public HashSet<long> paths;

    public HeightMapThreadInfo(Vector2 position, float[,] heightMap, HashSet<long> paths) {
        this.paths = paths;
        this.position = position;
        this.heightMap = heightMap;
    }
}
