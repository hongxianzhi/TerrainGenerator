using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ForestGenerator : MonoBehaviour {

    public GameObject treePrefab;

    public int mapWidth;
    public int mapHeight;
    public int numTrees;

    ArrayList trees = new ArrayList();

    public void Generate(float[,] heightMap, float waterLevel) {
        float topLeftX = (heightMap.GetLength(0) - 1) / -2f;
        float topLeftZ = (heightMap.GetLength(1) - 1) / 2f;

        for (int i = 0; i < numTrees; i++) {
            int x = Random.Range(0, mapWidth);
            int z = Random.Range(0, mapHeight);
            float y = heightMap[x, z] - 1;

            x = (int)topLeftX + x;
            z = (int)topLeftZ - z;

            if (y > waterLevel) {
                Vector3 position = new Vector3(x, y, z);
                GameObject tree = Instantiate(treePrefab, position, Quaternion.identity);

                float scale = Random.Range(1.5f, 2.5f);
                tree.transform.localScale = new Vector3(scale, scale, scale);
            
                trees.Add(tree);
            }
        }
    }

    public void Clear() {
        foreach (GameObject tree in trees) {
            DestroyImmediate(tree, true);
        }

        trees.Clear();
    }
}
