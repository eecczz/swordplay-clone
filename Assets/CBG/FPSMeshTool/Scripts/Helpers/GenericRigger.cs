using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GenericRigger : MonoBehaviour {
    public SkinnedMeshRenderer sourceRenderer;
    public Mesh sourceMesh;
    public Transform sourceBone;
    public SkinnedMeshRenderer targetRenderer;
    public Mesh targetMesh;
    public Transform targetBone;
    
    // [Button]
    // void BuildAvatar() {
    //     UnityEditor.AvatarMappingEditor.MakePoseValid();
    //     UnityEditor.AvatarSetupTool.MakePoseValid();
    // }


}