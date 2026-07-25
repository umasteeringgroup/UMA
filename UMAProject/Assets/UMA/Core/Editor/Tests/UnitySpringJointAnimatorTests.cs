#if UNITY_EDITOR

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UnitySpringJointAnimatorTests
    {
        private GameObject _root;
        private UnitySpringJointAnimator _animator;

        [TearDown]
        public void TearDown()
        {
            if (_animator != null)
            {
                Object.DestroyImmediate(_animator);
            }

            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void AutomaticChainCreatesAndReusesOwnedPhysics()
        {
            UMAData data = CreateSkeleton(
                out Transform anchor,
                out Transform first,
                out Transform second,
                out Transform excluded,
                out Transform excludedChild);

            _animator = ScriptableObject.CreateInstance<UnitySpringJointAnimator>();
            _animator.AnchorBoneName = anchor.name;
            _animator.AddBoneColliders = true;
            _animator.AddAnchorColliders = true;
            _animator.Spring = 72f;
            _animator.Damper = 8f;
            _animator.Chains.Add(new UnitySpringJointAnimator.ChainDefinition
            {
                AnchorBoneName = anchor.name,
                ExcludedBoneNames = new List<string> { excluded.name }
            });

            _animator.Initialize(data, null);

            Rigidbody anchorBody = anchor.GetComponent<Rigidbody>();
            Rigidbody firstBody = first.GetComponent<Rigidbody>();
            Rigidbody secondBody = second.GetComponent<Rigidbody>();
            SpringJoint firstJoint = first.GetComponent<SpringJoint>();
            SpringJoint secondJoint = second.GetComponent<SpringJoint>();

            Assert.NotNull(anchorBody);
            Assert.IsTrue(anchorBody.isKinematic);
            Assert.NotNull(firstBody);
            Assert.IsFalse(firstBody.isKinematic);
            Assert.NotNull(secondBody);
            Assert.AreSame(anchorBody, firstJoint.connectedBody);
            Assert.AreSame(firstBody, secondJoint.connectedBody);
            Assert.AreEqual(72f, firstJoint.spring);
            Assert.AreEqual(8f, firstJoint.damper);
            Assert.NotNull(anchor.GetComponent<SphereCollider>());
            Assert.NotNull(first.GetComponent<SphereCollider>());
            Assert.IsNull(excluded.GetComponent<Rigidbody>());
            Assert.IsNull(excludedChild.GetComponent<Rigidbody>());

            _animator.Initialize(data, null);

            Assert.AreSame(anchorBody, anchor.GetComponent<Rigidbody>());
            Assert.AreSame(firstBody, first.GetComponent<Rigidbody>());
            Assert.AreSame(firstJoint, first.GetComponent<SpringJoint>());
            Assert.AreEqual(1, first.GetComponents<Rigidbody>().Length);
            Assert.AreEqual(1, first.GetComponents<SpringJoint>().Length);
            Assert.AreEqual(
                1,
                first.GetComponents<UnitySpringJointAnimatorBone>().Length);

            _animator.MaxDepth = 1;
            _animator.Initialize(data, null);

            Assert.AreSame(firstBody, first.GetComponent<Rigidbody>());
            Assert.IsNull(second.GetComponent<Rigidbody>());
            Assert.IsNull(second.GetComponent<SpringJoint>());
            Assert.IsNull(second.GetComponent<UnitySpringJointAnimatorBone>());
        }

        [Test]
        [Category("UMA")]
        [Category("BoneAnimator")]
        public void ExplicitChainPreservesPreExistingRigidbodySettings()
        {
            UMAData data = CreateSkeleton(
                out Transform anchor,
                out Transform first,
                out Transform second,
                out _,
                out _);

            Rigidbody authoredBody = first.gameObject.AddComponent<Rigidbody>();
            authoredBody.mass = 7f;
            authoredBody.useGravity = false;
            authoredBody.constraints = RigidbodyConstraints.FreezeRotationY;

            _animator = ScriptableObject.CreateInstance<UnitySpringJointAnimator>();
            _animator.AnchorBoneName = anchor.name;
            _animator.SwingBoneNames = new List<string>
            {
                first.name,
                second.name
            };
            _animator.BoneMass = 0.25f;
            _animator.UseGravity = true;
            _animator.BoneConstraints = RigidbodyConstraints.None;

            _animator.Initialize(data, null);

            Assert.AreSame(authoredBody, first.GetComponent<Rigidbody>());
            Assert.AreEqual(7f, authoredBody.mass);
            Assert.IsFalse(authoredBody.useGravity);
            Assert.AreEqual(
                RigidbodyConstraints.FreezeRotationY,
                authoredBody.constraints);
            Assert.NotNull(first.GetComponent<SpringJoint>());
            Assert.AreSame(
                authoredBody,
                second.GetComponent<SpringJoint>().connectedBody);
        }

        private UMAData CreateSkeleton(
            out Transform anchor,
            out Transform first,
            out Transform second,
            out Transform excluded,
            out Transform excludedChild)
        {
            _root = new GameObject("SpringAnimatorTestRoot");
            anchor = CreateChild(_root.transform, "SpringAnchor");
            first = CreateChild(anchor, "SpringBone1");
            second = CreateChild(first, "SpringBone2");
            excluded = CreateChild(anchor, "ExcludedBranch");
            excludedChild = CreateChild(excluded, "ExcludedBranchChild");

            UMAData data = _root.AddComponent<UMAData>();
            data.umaRoot = _root;
            data.skeleton = new UMASkeleton(_root.transform);
            return data;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = Vector3.down * 0.1f;
            return child.transform;
        }
    }
}

#endif
