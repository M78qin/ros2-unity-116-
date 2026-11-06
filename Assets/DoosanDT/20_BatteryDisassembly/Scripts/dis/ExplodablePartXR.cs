using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.Events;

[RequireComponent(typeof(UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable))]
public class ExplodablePartXR : MonoBehaviour
{
    [Header("��ը����")]
    public Vector3 explosionDirection = Vector3.forward; // ��ը����
    [Range(0.1f, 5f)] public float explosionDistance = 1f; // ��ը����
    [HideInInspector] public int Order { get; private set; } // ��ը˳��

    private ExplosionControllerXR controller;
    private Vector3 initialPosition; // ��ʼλ��
    private Vector3 targetPosition; // ��ը���Ŀ��λ��
    private bool isExploded = false; // �Ƿ��Ѿ���ը
    private bool isExploding = false; // �Ƿ����ڱ�ը
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable interactable; // XR�������

    private void Awake()
    {
        interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        if (interactable == null)
        {
            interactable = gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
        }

        // ע��ѡ���¼�
        interactable.selectEntered.AddListener(OnSelected);

        // ȷ������ײ��
        if (!GetComponent<Collider>())
        {
            gameObject.AddComponent<BoxCollider>();
        }

        // ����Rigidbody��ֹ��������
        if (!GetComponent<Rigidbody>())
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }
    }

    // ����XR����ѡ��ʱ
    private void OnSelected(SelectEnterEventArgs args)
    {
        if (controller != null && !isExploded && !isExploding)
        {
            controller.TryExplodePart(this);
        }
    }

    // ���ÿ���������
    public void SetController(ExplosionControllerXR ctrl)
    {
        controller = ctrl;
    }

    // ���ñ�ը˳��
    public void SetOrder(int order)
    {
        Order = order;
    }

    // ��¼��ʼλ��
    public void SetInitialPosition()
    {
        initialPosition = transform.localPosition;
        targetPosition = initialPosition + explosionDirection.normalized * explosionDistance;
    }

    // ִ�б�ը
    public void Explode(UnityAction onComplete)
    {
        if (isExploded || isExploding) return;

        isExploding = true;
        StartCoroutine(ExplosionAnimation(onComplete));
    }

    // ��ը����Э��
    private System.Collections.IEnumerator ExplosionAnimation(UnityAction onComplete)
    {
        float elapsedTime = 0f;
        Vector3 startPos = transform.localPosition;
        float speed = controller.explosionSpeed;

        while (elapsedTime < 1f)
        {
            elapsedTime += Time.deltaTime * speed;
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime);
            transform.localPosition = Vector3.Lerp(startPos, targetPosition, t);
            yield return null;
        }

        transform.localPosition = targetPosition;
        isExploded = true;
        isExploding = false;
        onComplete?.Invoke();
    }

    // ���õ���ʼλ��
    public void ResetPosition()
    {
        StopAllCoroutines();
        transform.localPosition = initialPosition;
        isExploded = false;
        isExploding = false;
    }

    // ����Gizmo
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 dir = explosionDirection.normalized * explosionDistance;
        Gizmos.DrawLine(transform.localPosition, transform.localPosition + dir);
        Gizmos.DrawSphere(transform.localPosition + dir, 0.1f);
    }

    private void OnDestroy()
    {
        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnSelected);
        }
    }
}
