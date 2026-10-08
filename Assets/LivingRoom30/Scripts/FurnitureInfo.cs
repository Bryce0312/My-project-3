using UnityEngine;

namespace LivingRoom30
{
    [DisallowMultipleComponent]
    public sealed class FurnitureInfo : MonoBehaviour
    {
        [SerializeField] private string furnitureId;
        [SerializeField] private string englishName;

        public string FurnitureId => furnitureId;
        public string EnglishName => englishName;

        public void Configure(string id, string displayName)
        {
            furnitureId = id;
            englishName = displayName;
        }
    }
}
