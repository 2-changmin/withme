using UnityEngine;
using UnityEngine.EventSystems;

namespace Withme.UI
{
    public sealed class RoomPointerSurface:MonoBehaviour,IPointerClickHandler
    {
        public RoomFlow flow;
        public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)flow.PetScreen(e.position,e.pressEventCamera);}
    }
}
