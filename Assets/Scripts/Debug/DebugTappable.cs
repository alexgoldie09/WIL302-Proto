using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class DebugTappable : MonoBehaviour, IHandler
{
    [Header("Tappable Debug")]
    [SerializeField] private LayerMask interactableLayer;
    [SerializeField] private ItemDefinition itemSO;
    [SerializeField] private int amountToAdd = 1;
    [SerializeField] private bool canMove = false;
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;
    
    [Header("Quest Reporting")]
    [SerializeField] private bool reportQuestProgress = false;
    [SerializeField] private QuestObjectiveType collectQuestType = QuestObjectiveType.CollectAnimalItem;
    [SerializeField] private string questAnimalType = "";

    [Header("Alert Debug")]
    [SerializeField] private bool canAlert = false;
    [SerializeField] private AlertType debugAlertType = AlertType.Hungry;
    [SerializeField] private Vector2 alertOffset = new Vector2(0.75f, 1f);
    [SerializeField] private float minAlertInterval = 4f;
    [SerializeField] private float maxAlertInterval = 10f;
    
    [Header("Destroy Debug")]
    [SerializeField] private bool canDestroy = false;
    [SerializeField] private float destroyTime = 6f;

    private float _nextAlertTime;

    private void OnEnable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap += HandleWorldTap;

        ScheduleNextAlert();
    }

    private void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnWorldTap -= HandleWorldTap;
    }

    private void Start()
    {
        if(canMove && startPoint != null)
            transform.position = startPoint.position;
        
        if (canDestroy)
            Destroy(gameObject, destroyTime);
    }
    
    private void Update()
    {
        if (canMove && endPoint != null && startPoint != null)
        {
            transform.position = Vector2.Lerp(startPoint.position, endPoint.position, (Mathf.Sin(Time.time * moveSpeed) + 1f) / 2f);
        }
        
        if (AlertManager.Instance == null || !canAlert) return;

        if (Time.time >= _nextAlertTime)
        {
            AlertManager.Instance.ShowAlert(gameObject, debugAlertType, alertOffset);
            ScheduleNextAlert();
        }
    }

    private void HandleWorldTap(Vector2 worldPos)
    {
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero, 0f, interactableLayer);
        if (hit.collider != null && hit.collider.gameObject == gameObject)
            OnTapped();
    }

    public void OnTapped()
    {
        if (itemSO == null)
        {
            Debug.LogWarning("[DebugTappable] No ItemDefinition assigned!");
            return;
        }

        if (PlayerInventory.Instance == null)
        {
            Debug.LogWarning("[DebugTappable] No PlayerInventory instance found in scene!");
            return;
        }
        
        AudioManager.Instance?.PlaySFX("collect_sound", 0.4f);

        PlayerInventory.Instance.Add(itemSO, amountToAdd);
        
        if (reportQuestProgress)
            QuestManager.Instance?.RecordProgress(
                collectQuestType, itemSO.ItemName, amountToAdd, questAnimalType);

        Debug.Log($"[DebugTappable] Picked up {amountToAdd}x {itemSO.ItemName}. " +
                  $"Total now: {PlayerInventory.Instance.GetCount(itemSO)}");

        Destroy(gameObject);
    }

    private void ScheduleNextAlert()
    {
        _nextAlertTime = Time.time + Random.Range(minAlertInterval, maxAlertInterval);
    }
}