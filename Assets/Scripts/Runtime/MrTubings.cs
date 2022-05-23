using UnityEngine;
using Unity.Mathematics;

using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;

using Range = UnityEngine.RangeAttribute;

namespace KageKirin.Mr_Tubings
{
    [AddComponentMenu("Skeleton Visualizer/Mr. Tubings")]
    public class MrTubings : MonoBehaviour
    {
        [Tooltip("Cylinder radius")]
        public float radius = 0.01f;

        [Tooltip("Leaf node axis length")]
        public float leafAxisLength = 0.01f;

        [Tooltip("Subdivision of cylinder disc")]
        [Range(3, 1000)]
        public uint radialSubdivisions = 6;

        [Tooltip("Subdivision of cylinder tube")]
        [Range(2, 1000)]
        public uint lateralSubdivisions = 3;

        [ContextMenuItem("Create Mesh", "CreateMesh")]
        public Mesh mesh;

        void CreateMesh()
        {
            Transform[] transforms = gameObject.GetComponentsInChildren<Transform>(); //< iterates depth-first
            Debug.Log($"{transforms.Length} transforms");
            foreach (var t in transforms)
            {
                Debug.Log($"{t.name}");
            }

            // prepare data (compute once, ahead of setting, instead of during iteration)
            float2[] radialPositions = new float2[radialSubdivisions];
            float tauBySubdiv = 2 * math.PI / (float)radialSubdivisions;
            for (uint i = 0; i < radialSubdivisions; i++)
            {
                float angleI = tauBySubdiv * (float)i;
                radialPositions[i] = math.float2(math.sin(angleI), math.cos(angleI));
            }

            float[] lateralPositions = new float[lateralSubdivisions + 1];
            for (uint i = 0; i < lateralSubdivisions + 1; i++)
            {
                lateralPositions[i] = (float)i / (float)lateralSubdivisions;
            }

            List<float3> vertices = new List<float3>();
            List<float3> normals = new List<float3>();
            List<float2> uvs = new List<float2>();
            List<BoneWeight> boneWeights = new List<BoneWeight>();
            List<int> indices = new List<int>();
            List<float4x4> bindPoses = new List<float4x4>();

            Transform rootTransform = transforms[0];
            float4x4 mWorldToRootSpace = rootTransform.worldToLocalMatrix;

            for (int tIdx = 0; tIdx < transforms.Length; tIdx++)
            {
                ref Transform transform = ref transforms[tIdx];
                bindPoses.Add(transform.worldToLocalMatrix * transforms[0].localToWorldMatrix);

                if (transform.parent == null)
                {
                    continue;
                }

                float4x4 mWorldToLocal = transform.worldToLocalMatrix;
                float4x4 mLocalToWorld = transform.localToWorldMatrix;
                float4x4 mLocalToRoot = math.mul(mWorldToRootSpace, mLocalToWorld);
                float3x3 mLocalToRoot3x3 = math.float3x3(mLocalToRoot);

                float3 localPositionInLocalSpace = math.float3(0, 0, 0); //math.mul(mWorldToLocal, float4(transform.position, 1.0f));
                float3 parentPositionInLocalSpace =
                    math.mul(mWorldToLocal, math.float4(transform.parent.position, 1.0f)).xyz;

                float3 axis = parentPositionInLocalSpace - localPositionInLocalSpace; //or basically parentPositionInLocalSpace

                GenerateVertexDataForBone(
                    ref transform,
                    tIdx,
                    axis,
                    mWorldToRootSpace,
                    radialPositions,
                    lateralPositions,
                    vertices,
                    normals,
                    uvs,
                    boneWeights,
                    indices
                );

                // add small axis for leaf nodes
                if (transform.childCount == 0 && leafAxisLength >= 0.0f)
                {
                    float3 leafAxis = math.float3(0, leafAxisLength, 0);

                    GenerateVertexDataForBone(
                        ref transform,
                        tIdx,
                        leafAxis,
                        mWorldToRootSpace,
                        radialPositions,
                        lateralPositions,
                        vertices,
                        normals,
                        uvs,
                        boneWeights,
                        indices
                    );
                }
            }

            // create new mesh
            var skinnedMeshRenderer = gameObject.GetOrAddComponent<SkinnedMeshRenderer>();
            mesh = skinnedMeshRenderer.sharedMesh ?? new Mesh();
            if (skinnedMeshRenderer.sharedMesh == null)
            {
                skinnedMeshRenderer.sharedMesh = mesh;
            }
            mesh.name = "MrTubings_" + rootTransform.name;

            // check data is correct
            Assert.That(bindPoses.Count, Is.EqualTo(transforms.Length));
            Assert.That(
                boneWeights.Where(bw => bw.boneIndex0 >= transforms.Length).Count,
                Is.EqualTo(0)
            );

            // set mesh data
            mesh.SetVertices(vertices.Select(v => (Vector3)v).ToList());
            mesh.SetNormals(normals.Select(n => (Vector3)n).ToList());
            mesh.SetUVs(0, uvs.Select(t => (Vector2)t).ToList());
            mesh.boneWeights = boneWeights.ToArray();
            mesh.bindposes = bindPoses.Select(m => (Matrix4x4)m).ToArray();
            mesh.SetIndices(
                indices,
                0,
                indices.Count,
                MeshTopology.Quads,
                0, //submesh 0
                true,
                0
            );

            // set skin renderer data
            skinnedMeshRenderer.bones = transforms;
            skinnedMeshRenderer.rootBone = rootTransform;
        }

        void GenerateVertexDataForBone(
            ref Transform transform,
            in int transformIndex,
            in float3 axis,
            in float4x4 mWorldToRootSpace,
            in float2[] radialPositions,
            in float[] lateralPositions,
            List<float3> vertices,
            List<float3> normals,
            List<float2> uvs,
            List<BoneWeight> boneWeights,
            List<int> indices
        )
        {
            float4x4 mWorldToLocal = transform.worldToLocalMatrix;
            float4x4 mLocalToWorld = transform.localToWorldMatrix;
            float4x4 mLocalToRoot = math.mul(mWorldToRootSpace, mLocalToWorld);
            float3x3 mLocalToRoot3x3 = math.float3x3(mLocalToRoot);

            float axisLength = math.length(axis);

            for (uint lateral = 0; lateral < lateralSubdivisions; lateral++)
            {
                var nextLateral = lateral + 1; //< safe since we have lateralSubdivisions+1 values

                for (uint radial = 0; radial < radialSubdivisions; radial++)
                {
                    var nextRadial = (radial + 1) % radialSubdivisions;

                    float3[] quadVertices = new float3[4]
                    {
                        // note: xzy swizzle required, but float3(float2, float) makes this easier to read
                        // note: then translate by lateral factor, here z * axis
                        math.float3(
                            radius * radialPositions[nextRadial],
                            lateralPositions[lateral]
                        ),
                        math.float3(radius * radialPositions[radial], lateralPositions[lateral]),
                        math.float3(
                            radius * radialPositions[radial],
                            lateralPositions[nextLateral]
                        ),
                        math.float3(
                            radius * radialPositions[nextRadial],
                            lateralPositions[nextLateral]
                        ),
                    };

                    float3[] quadNormals = new float3[4]
                    {
                        // note: xzy swizzle required as well
                        // note: normalization required as well
                        math.float3(radialPositions[nextRadial], 0.0f),
                        math.float3(radialPositions[radial], 0.0f),
                        math.float3(radialPositions[radial], 0.0f),
                        math.float3(radialPositions[nextRadial], 0.0f),
                    };

                    float2[] quadTexcoords = new float2[4]
                    {
                        math.float2(
                            (float)(radial + 1) / (float)radialSubdivisions,
                            (float)lateral / (float)lateralSubdivisions
                        ),
                        math.float2(
                            (float)(radial) / (float)radialSubdivisions,
                            (float)lateral / (float)lateralSubdivisions
                        ),
                        math.float2(
                            (float)(radial) / (float)radialSubdivisions,
                            (float)(lateral + 1) / (float)lateralSubdivisions
                        ),
                        math.float2(
                            (float)(radial + 1) / (float)radialSubdivisions,
                            (float)(lateral + 1) / (float)lateralSubdivisions
                        ),
                    };

                    BoneWeight[] quadWeights = new BoneWeight[4]
                    {
                        new BoneWeight()
                        {
                            boneIndex0 = transformIndex,
                            boneIndex1 = 0,
                            boneIndex2 = 0,
                            boneIndex3 = 0,
                            weight0 = 1,
                            weight1 = 0,
                            weight2 = 0,
                            weight3 = 0,
                        },
                        new BoneWeight()
                        {
                            boneIndex0 = transformIndex,
                            boneIndex1 = 0,
                            boneIndex2 = 0,
                            boneIndex3 = 0,
                            weight0 = 1,
                            weight1 = 0,
                            weight2 = 0,
                            weight3 = 0,
                        },
                        new BoneWeight()
                        {
                            boneIndex0 = transformIndex,
                            boneIndex1 = 0,
                            boneIndex2 = 0,
                            boneIndex3 = 0,
                            weight0 = 1,
                            weight1 = 0,
                            weight2 = 0,
                            weight3 = 0,
                        },
                        new BoneWeight()
                        {
                            boneIndex0 = transformIndex,
                            boneIndex1 = 0,
                            boneIndex2 = 0,
                            boneIndex3 = 0,
                            weight0 = 1,
                            weight1 = 0,
                            weight2 = 0,
                            weight3 = 0,
                        },
                    };

                    int[] quadIndices = new int[4]
                    {
                        indices.Count,
                        indices.Count + 1,
                        indices.Count + 2,
                        indices.Count + 3,
                    };

                    // offset, swizzle and transform quadVertices to rootSpace
                    for (int i = 0; i < quadVertices.Length; i++)
                    {
                        ref float3 vertex = ref quadVertices[i];
                        vertex = vertex.z * axis + math.float3(vertex.x, 0, vertex.y);
                        vertex = math.mul(mLocalToRoot, math.float4(vertex, 1.0f)).xyz;
                    }

                    // swizzle and transform quadNormals to rootSpace
                    for (int i = 0; i < quadNormals.Length; i++)
                    {
                        ref float3 normal = ref quadNormals[i];
                        normal = math.mul(mLocalToRoot3x3, math.normalize(normal.xzy));
                    }

                    vertices.AddRange(quadVertices);
                    normals.AddRange(quadNormals);
                    uvs.AddRange(quadTexcoords);
                    boneWeights.AddRange(quadWeights);
                    indices.AddRange(quadIndices);
                }
            }
        }
    }
}
