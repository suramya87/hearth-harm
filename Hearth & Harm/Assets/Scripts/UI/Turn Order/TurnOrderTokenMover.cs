using UnityEngine;

public class TurnOrderTokenMover : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 12f;

    private RectTransform rectTransform;
    private Vector2 targetPosition;
    private bool hasTarget;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    public void SetTargetPosition(Vector2 pos)
    {
        targetPosition = pos;
        hasTarget = true;
    }

    private void Update()
    {
        if (!hasTarget || rectTransform == null) return;

        rectTransform.anchoredPosition = Vector2.Lerp(
            rectTransform.anchoredPosition,
            targetPosition,
            moveSpeed * Time.deltaTime
        );

        if (Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < 0.5f)
        {
            rectTransform.anchoredPosition = targetPosition;
        }
    }
}