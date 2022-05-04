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

namespace anatawa12.BoneFixer.Editor
{
    public class BoneFixer : EditorWindow
    {
        [CanBeNull] private SkinnedMeshRenderer broken;
        [CanBeNull] private SkinnedMeshRenderer model;

        [MenuItem("anatawa12/BoneFixer")]
        public static void ShowWindow()
        {
            GetWindow<BoneFixer>();
        }

        void OnGUI()
        {
            broken = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("broken", broken, typeof(SkinnedMeshRenderer), true);
            model = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("model", model, typeof(SkinnedMeshRenderer), true);

            if (GUILayout.Button("Fix Bones"))
            {
                if (broken is null || model is null)
                {
                    EditorUtility.DisplayDialog("ERROR", "Please select SkinedMeshRenderer", "OK");
                    return;
                }

                try
                {
                    if (DoFix(broken, model))
                        EditorUtility.DisplayDialog("succeed", "Fixing bones succeed!", "OK");
                }
                catch (FixException e)
                {
                    EditorUtility.DisplayDialog("ERROR", e.Message, "OK");
                    return;
                }
            }
        }

        private static bool DoFix(SkinnedMeshRenderer broken, SkinnedMeshRenderer model)
        {
            if (broken.sharedMesh != model.sharedMesh)
            {
                throw new FixException("Mesh differ");
            }

            var fixBones = ToDictionary(broken.bones.Select((x, i) => (x.gameObject.name, i)),
                name => throw new FixException($"name of bones of broken duplicated: {name}"));
            var modelBones = ToDictionary(model.bones.Select((x, i) => (x.gameObject.name, i)),
                name => throw new FixException($"name of bones of broken duplicated: {name}"));

            var removed = fixBones.Where(e => !modelBones.ContainsKey(e.Key)).ToList();
            var added = new HashSet<KeyValuePair<string, int>>(modelBones.Where(e => !fixBones.ContainsKey(e.Key)));

            // bones names and arranging is same: nop
            if (fixBones == modelBones) return true;
            if (removed.Count != 0 || added.Count != 0)
            {
                if (!EditorUtility.DisplayDialog("Confirm",
                        $"The following bones will be removed: {string.Join(", ", removed.Select(x => x.Key))}\n" +
                        $"The following bones will be added: {string.Join(", ", added.Select(x => x.Key))}",
                        "OK",
                        "Cancel"))
                {
                    return false;
                }
            }

            // remove bones
            foreach (var pair in removed)
            {
                var bone = broken.bones[pair.Value];
                while (bone.childCount > 0)
                {
                    // world location will be kept automatically so What this need to do is
                    // moving child bone to parent of removing bone
                    bone.GetChild(0).SetParent(bone.parent);
                }

                Destroy(bone.gameObject);
            }

            var bonesMap = fixBones
                .Where(e => modelBones.ContainsKey(e.Key))
                .ToDictionary(e => e.Key, e => broken.bones[e.Value]);

            // add bones
            while (added.Count != 0)
            {
                foreach (var pair in added)
                {
                    var modelBone = model.bones[pair.Value];
                    if (!bonesMap.TryGetValue(modelBone.parent.gameObject.name, out var newParent)) continue;
                    added.Remove(pair);
                    var newBone = bonesMap[pair.Key] = new GameObject(pair.Key).transform;
                    newBone.SetParent(newParent, false);
                    newBone.localPosition = modelBone.localPosition;
                    newBone.localScale = modelBone.localScale;
                    newBone.localRotation = modelBone.localRotation;
                    break;
                }
            }

            // rearrange bones
            var newBoneArray = new Transform[model.bones.Length];

            for (int i = 0; i < newBoneArray.Length; i++)
            {
                newBoneArray[i] = bonesMap[model.bones[i].name];
            }

            broken.bones = newBoneArray;

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
