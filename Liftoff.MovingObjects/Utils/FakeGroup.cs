using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Liftoff.MovingObjects.Utils;

internal class FakeGroup
{
    public static IDisposable GroupObjects(GameObject rootGameObject, List<GameObject> childs, bool destroyChilds)
    {
        var ctx = new FakeContext();
        foreach (var gameObject in childs)
        {
            Cleanup(rootGameObject);
            var fakeChild = gameObject.AddComponent<FakeChild>();
            fakeChild.fakeParent = rootGameObject.transform;
            ctx.childs.Add(fakeChild);
        }

        if (destroyChilds)
        {
            Cleanup(rootGameObject);
            var fakeParent = rootGameObject.AddComponent<FakeParent>();
            fakeParent.childs = childs;
            ctx.parent = fakeParent;
        }

        return ctx;
    }

    public static IReadOnlyList<GameObject> GetChilds(GameObject gameObject)
    {
        var parent = gameObject.GetComponent<FakeParent>();
        return parent != null ? parent.childs : null;
    }

    private static void Cleanup(GameObject gameObject)
    {
        var child = gameObject.GetComponent<FakeChild>();
        if (child != null)
            Object.Destroy(child);
        var parent = gameObject.GetComponent<FakeParent>();
        if (parent != null)
            Object.Destroy(parent);
    }

    private class FakeContext : IDisposable
    {
        public readonly List<FakeChild> childs = new();
        public FakeParent parent;

        public void Dispose()
        {
            if (parent != null)
                Object.Destroy(parent);
            foreach (var child in childs)
                Object.Destroy(child);
        }
    }

    private class FakeParent : MonoBehaviour
    {
        public IReadOnlyList<GameObject> childs;

        private void OnDestroy()
        {
            foreach (var children in childs)
                Destroy(children);
        }
    }

    private class FakeChild : MonoBehaviour
    {
        public Transform fakeParent;

        private Matrix4x4 parentMatrix;

        private Vector3 startChildPosition;
        private Quaternion startChildRotationQ;


        private Vector3 startParentPosition;
        private Quaternion startParentRotationQ;

        private void Start()
        {
            startParentPosition = fakeParent.position;
            startParentRotationQ = fakeParent.rotation;

            startChildRotationQ = transform.rotation;

            // Store the member's offset in the parent's rotated frame only — no scale term. The group
            // follows the anchor's translation and rotation rigidly, but NOT its scale: resizing the
            // anchor (e.g. a scalable block) used to feed fakeParent.lossyScale into the matrix and
            // slide every member toward/away from it (honk: "the whole group scales with it"). Members
            // never had their own size changed, only their position, so this only respaced the group —
            // now scaling a member is a purely local change.
            startChildPosition = Quaternion.Inverse(fakeParent.rotation) * (transform.position - startParentPosition);
        }

        private void Update()
        {
            parentMatrix = Matrix4x4.TRS(fakeParent.position, fakeParent.rotation, Vector3.one);
            transform.position = parentMatrix.MultiplyPoint3x4(startChildPosition);
            transform.rotation = fakeParent.rotation * Quaternion.Inverse(startParentRotationQ) * startChildRotationQ;
        }
    }
}