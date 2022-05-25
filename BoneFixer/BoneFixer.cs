/* 
 * Copyright (c) anatawa12 2022 All Rights Reserved.
 * This file is published under MIT License.
 * See https://opensource.org/licenses/MIT for full license code.
 * THIS SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace anatawa12.BoneFixer.Editor
{
    public class BoneFixer : EditorWindow
    {
        [CanBeNull] private SkinnedMeshRenderer broken;
        [CanBeNull] private SkinnedMeshRenderer model;
        // if keep object
        private List<(string name, int idx, bool keep)> removed = new List<(string, int, bool)>();
        // if the value is null, 
        private List<(string name, Transform bone)> mapping = new List<(string, Transform)>();
        private Vector2 _boneListScrollPosition = Vector2.zero;

        [MenuItem("anatawa12/BoneFixer")]
        public static void ShowWindow()
        {
            GetWindow<BoneFixer>();
        }

        void OnGUI()
        {
            bool isChanged = false;
            SkinnedMeshRenderer old;
            old = broken;
            broken = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("broken", broken, typeof(SkinnedMeshRenderer),
                true);
            isChanged |= old != broken;

            old = model;
            model = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("model", model, typeof(SkinnedMeshRenderer), true);
            isChanged |= old != model;

            bool modelsNonNull = !(broken is null) && !(model is null);
            bool readyToFix = modelsNonNull && broken.sharedMesh == model.sharedMesh;
            if (readyToFix && (isChanged || mapping.Count != model.bones.Length))
            {
                (removed, mapping) = FindRemovedAdded(broken, model);
            }

            if (modelsNonNull && !readyToFix)
            {
                EditorGUILayout.LabelField("ERROR: Mesh Mismatch");
            }

            if (readyToFix)
            {
                EditorGUILayout.LabelField("bones");
                _boneListScrollPosition = EditorGUILayout.BeginScrollView(_boneListScrollPosition);
                if (mapping.Count != 0)
                {
                    EditorGUILayout.LabelField("mapping bones: ");
                    for (int i = 0; i < mapping.Count; i++)
                    {
                        var tuple = mapping[i];
                        tuple.bone =
                            (Transform)EditorGUILayout.ObjectField(tuple.name, tuple.bone, typeof(Transform), true);
                        mapping[i] = tuple;
                    }
                }

                if (removed.Count != 0)
                {
                    EditorGUILayout.LabelField("there's removed bones");
                    EditorGUILayout.LabelField("check to keep GameObject");
                    for (int i = 0; i < removed.Count; i++)
                    {
                        var tuple = removed[i];
                        tuple.keep = EditorGUILayout.Toggle(tuple.name, tuple.keep);
                        removed[i] = tuple;
                    }
                }

                EditorGUILayout.EndScrollView();
            }

            if (readyToFix)
            {

                if (GUILayout.Button("Fix Bones"))
                {
                    try
                    {
                        if (DoFix(broken, model))
                            EditorUtility.DisplayDialog("succeed", "Fixing bones succeed!", "OK");
                    }
                    catch (FixException e)
                    {
                        EditorUtility.DisplayDialog("ERROR", e.Message, "OK");
                    }
                }
            }
        }

        private static (List<(string, int, bool)>, List<(string, Transform)>) FindRemovedAdded(
            SkinnedMeshRenderer broken, SkinnedMeshRenderer model)
        {
            var fixBones = BoneIndicesMap(broken.bones, "broken");
            var modelBones = new HashSet<string>(model.bones.Select((x, i) => x.gameObject.name));

            return (
                fixBones
                    .Where(e => !modelBones.Contains(e.Key))
                    .OrderBy(e => e.Key)
                    .Select(e => (e.Key, e.Value, false))
                    .ToList(),
                model.bones.Select(modelBone =>
                {
                    var name = modelBone.gameObject.name;
                    return (name, fixBones.TryGetValue(name, out var fixBoneIdx) ? broken.bones[fixBoneIdx] : null);
                }).ToList()
            );
        }

        private static Dictionary<string, int> BoneIndicesMap(Transform[] bones, string of)
        {
            return ToDictionary(bones
                    .Select((x, i) => (x, i))
                    .Where(x => x.x != null)
                    .Select(x => (x.x.gameObject.name, x.i)),
                name => throw new FixException($"name of bones of {of} duplicated: {name}"));
        }

        // ReSharper disable ParameterHidesMember
        private bool DoFix(SkinnedMeshRenderer broken, SkinnedMeshRenderer model)
        {
            var fixBones = BoneIndicesMap(broken.bones, "broken");
            var modelBones = BoneIndicesMap(model.bones, "model");
            // ReSharper restore ParameterHidesMember

            // bones names and arranging is same: nop
            if (fixBones == modelBones) return true;

            // remove bones
            foreach (var pair in removed)
            {
                if (pair.keep) continue;
                var bone = broken.bones[pair.idx];
                while (bone.childCount > 0)
                {
                    // world location will be kept automatically so What this need to do is
                    // moving child bone to parent of removing bone
                    bone.GetChild(0).SetParent(bone.parent);
                }

                Destroy(bone.gameObject);
            }

            var bonesMap = mapping
                .Where(e => e.bone != null)
                .ToDictionary(e => e.name, e => e.bone);

            // add bones
            bool thereIsNull;
            do
            {
                thereIsNull = false;

                for (int i = 0; i < mapping.Count; i++)
                {
                    // ReSharper disable once LocalVariableHidesMember
                    var (name, bone) = mapping[i];
                    if (bone == null)
                    {
                        var modelBone = model.bones[i];
                        if (!bonesMap.TryGetValue(modelBone.parent.gameObject.name, out var newParent))
                        {
                            thereIsNull = true;
                            continue;
                        }

                        var newBone = new GameObject(name).transform;
                        newBone.SetParent(newParent, false);
                        newBone.localPosition = modelBone.localPosition;
                        newBone.localScale = modelBone.localScale;
                        newBone.localRotation = modelBone.localRotation;
                        mapping[i] = (name, newBone);
                        bonesMap[name] = newBone;
                    }
                }
            } while (thereIsNull);

            // rearrange bones
            broken.bones = mapping.Select(x => x.bone).ToArray();

            EditorUtility.SetDirty(broken);

            return true;
        }

        private static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(
            IEnumerable<(TKey, TValue)> enumerable,
            Action<TKey> onDuplicate = null
        ) {
            var dict = new Dictionary<TKey, TValue>();
            foreach (var (key, value) in enumerable)
            {
                if (dict.ContainsKey(key))
                    onDuplicate?.Invoke(key);
                dict[key] = value;
            }

            return dict;
        }

        private class FixException : Exception
        {
            public FixException(string message) : base(message)
            {
            }
        }
    }
}
