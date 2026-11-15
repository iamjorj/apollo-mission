// An instrument name inside Columbia, placed at the Smithsonian scan's own annotation point.
using UnityEngine;

public class CabinLabel : MonoBehaviour
{
    public string Title;
    [TextArea] public string Line;
    public bool Control;                 // a problem's switch or button: always clickable when it is on screen
}
