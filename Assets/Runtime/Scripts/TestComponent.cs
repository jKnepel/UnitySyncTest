using System;
using UnityEngine;

namespace Toph.GLTFSerializer.Test
{
    public class TestComponent : MonoBehaviour
    {
#pragma warning disable CS0414
        [SerializeField]
        private int Numberrr = 42;

        [SerializeField]
        private string Foo = "XXX";

        [SerializeField]
        private bool x = false;

        [SerializeField]
        private Color color = Color.red;

        [SerializeField] private GLTFData data = new GLTFData();
#pragma warning restore

        [Serializable]
        public class GLTFData
        {
            [SerializeField] private int number = 42;

            public int Number { get { return number; } set { number = value; } }
        }
    }
}