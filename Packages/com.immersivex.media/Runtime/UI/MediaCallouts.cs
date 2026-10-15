using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ImmersiveX.Media
{
    /// <summary>
    /// Callout cards in the room for media that brings them (<see cref="IMediaAnnotations"/>): a card beside each part being
    /// explained, facing the user, with a line to a ring on the part. Cards fade in and out with the content, keep clear of
    /// each other, glide rather than jump when the part moves, and grow with distance so the text stays readable.
    /// They only show information: rays and hands pass through them.
    /// </summary>
    [AddComponentMenu("")]
    public sealed class MediaCallouts : MonoBehaviour
    {
        const float Width = 240f; // millimetres
        const float Pad = 12f;
        const float Glide = 0.12f;

        static readonly Color Glass = new Color(0.035f, 0.05f, 0.075f, 0.86f);
        static readonly Color Amber = new Color(1f, 0.68f, 0.37f);
        static readonly Color Ink = new Color(0.93f, 0.95f, 0.98f);
        static readonly Color Muted = new Color(0.72f, 0.77f, 0.84f);

        sealed class Card
        {
            public GameObject Root;
            public CanvasGroup Group;
            public RectTransform Canvas;
            public Text Title, Body, Stat;
            public LineRenderer Line;
            public Transform Ring;
            public Vector3 Position;
            public bool Placed;
            public float Height;
        }

        readonly Dictionary<string, Card> _cards = new Dictionary<string, Card>();
        readonly HashSet<string> _seen = new HashSet<string>();
        readonly List<(Card card, Vector3 target, float scale)> _layout = new List<(Card, Vector3, float)>();
        Material _lineMaterial;

        public static MediaCallouts Create(string name)
        {
            var go = new GameObject($"Callouts ({name})");
            return go.AddComponent<MediaCallouts>();
        }

        /// <summary>Show <paramref name="callouts"/> for <paramref name="viewer"/>; <paramref name="shown"/> false hides them all.</summary>
        public void Show(IReadOnlyList<MediaCallout> callouts, Transform viewer, bool shown)
        {
            _seen.Clear();
            _layout.Clear();
            if (shown && viewer != null && callouts != null)
            {
                foreach (var c in callouts)
                {
                    if (c.Visibility <= 0.01f)
                        continue;
                    _seen.Add(c.Id);
                    if (!_cards.TryGetValue(c.Id, out var card))
                        _cards[c.Id] = card = Build(c);
                    card.Group.alpha = c.Visibility;
                    _layout.Add((card, Target(c.Anchor, viewer, out var scale), scale));
                    card.Ring.position = c.Anchor;
                    card.Ring.rotation = Quaternion.LookRotation(c.Anchor - viewer.position, Vector3.up);
                    card.Ring.localScale = Vector3.one * scale;
                }
            }

            // keep cards apart: lower ones move down until they clear the ones above
            _layout.Sort((a, b) => b.target.y.CompareTo(a.target.y));
            for (var i = 0; i < _layout.Count; i++)
            {
                var (card, target, scale) = _layout[i];
                for (var j = 0; j < i; j++)
                {
                    var (other, otherTarget, otherScale) = _layout[j];
                    var gapY = (other.Height * otherScale + card.Height * scale) * 0.0005f + 0.02f;
                    var flat = new Vector2(target.x - otherTarget.x, target.z - otherTarget.z).magnitude;
                    if (flat < Width * 0.001f * Mathf.Max(scale, otherScale) && otherTarget.y - target.y < gapY)
                        target.y = otherTarget.y - gapY;
                }

                _layout[i] = (card, target, scale);
                var glide = card.Placed ? 1f - Mathf.Exp(-Time.unscaledDeltaTime / Glide) : 1f;
                card.Position = Vector3.Lerp(card.Position, target, glide);
                card.Placed = true;
                card.Root.SetActive(true);
                card.Root.transform.position = card.Position;
                card.Root.transform.rotation = Quaternion.LookRotation(card.Position - viewer.position, Vector3.up);
                card.Root.transform.localScale = Vector3.one * scale;
                // the line from the card's nearer side to the ring
                var right = card.Root.transform.right;
                var edge = card.Position + right * (Width * 0.0005f * scale) * Mathf.Sign(Vector3.Dot(card.Ring.position - card.Position, right));
                card.Line.SetPosition(0, edge);
                card.Line.SetPosition(1, card.Ring.position);
                card.Line.widthMultiplier = 0.0018f * scale * card.Group.alpha;
                card.Line.enabled = card.Group.alpha > 0.05f;
            }

            foreach (var pair in _cards)
            {
                if (_seen.Contains(pair.Key))
                    continue;
                pair.Value.Root.SetActive(false);
                pair.Value.Placed = false;
            }
        }

        /// <summary>Beside the anchor, towards the user and to their right (left past the middle), scaled with distance.</summary>
        static Vector3 Target(Vector3 anchor, Transform viewer, out float scale)
        {
            var toAnchor = anchor - viewer.position;
            var distance = toAnchor.magnitude;
            scale = Mathf.Clamp(distance / 1.1f, 0.75f, 2.4f);
            var forward = Vector3.ProjectOnPlane(toAnchor, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            var side = Vector3.Dot(toAnchor, viewer.right) > 0.25f ? -1f : 1f; // keep cards inside the user's view
            return anchor + right * side * (0.17f * scale) + Vector3.up * (0.07f * scale) - forward * (0.12f * scale);
        }

        Card Build(MediaCallout c)
        {
            var card = new Card { Root = new GameObject($"Callout {c.Id}") };
            card.Root.transform.SetParent(transform, false);
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            canvasObject.transform.SetParent(card.Root.transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            card.Group = canvasObject.GetComponent<CanvasGroup>();
            card.Group.interactable = false;
            card.Group.blocksRaycasts = false;
            card.Canvas = (RectTransform)canvasObject.transform;
            card.Canvas.localScale = Vector3.one * 0.001f;

            card.Title = MakeText(card.Canvas, "Title", c.Title, 17, FontStyle.Bold, Ink);
            card.Body = MakeText(card.Canvas, "Body", c.Body, 12, FontStyle.Normal, Muted);
            card.Stat = string.IsNullOrEmpty(c.Stat) ? null : MakeText(card.Canvas, "Stat", c.Stat, 12, FontStyle.Bold, Amber);
            var inner = Width - Pad * 2f - 4f;
            var title = card.Title.cachedTextGenerator.GetPreferredHeight(c.Title ?? string.Empty, card.Title.GetGenerationSettings(new Vector2(inner, 0f)));
            var body = card.Body.cachedTextGenerator.GetPreferredHeight(c.Body ?? string.Empty, card.Body.GetGenerationSettings(new Vector2(inner, 0f)));
            var stat = card.Stat != null ? card.Stat.cachedTextGenerator.GetPreferredHeight(c.Stat, card.Stat.GetGenerationSettings(new Vector2(inner, 0f))) : 0f;
            var height = Pad + title + 4f + body + (card.Stat != null ? 6f + stat : 0f) + Pad;
            card.Height = height;
            card.Canvas.sizeDelta = new Vector2(Width, height);

            var background = WorldUi.Rect(card.Canvas, "Background", Vector2.zero, new Vector2(Width, height), Glass);
            background.sprite = UiSprites.Panel;
            background.type = Image.Type.Sliced;
            background.transform.SetAsFirstSibling();
            var stripe = WorldUi.Rect(card.Canvas, "Stripe", new Vector2(-Width * 0.5f + 2f, 0f), new Vector2(3f, height - 6f), Amber);
            stripe.transform.SetSiblingIndex(1);
            var y = height * 0.5f - Pad;
            Place(card.Title, inner, y, title);
            y -= title + 4f;
            Place(card.Body, inner, y, body);
            y -= body + 6f;
            if (card.Stat != null)
                Place(card.Stat, inner, y, stat);

            var ring = new GameObject("Ring", typeof(RectTransform), typeof(Canvas));
            ring.transform.SetParent(card.Root.transform, false);
            ring.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var ringRect = (RectTransform)ring.transform;
            ringRect.sizeDelta = new Vector2(16f, 16f);
            ringRect.localScale = Vector3.one * 0.001f;
            var ringImage = WorldUi.Rect(ringRect, "Dot", Vector2.zero, new Vector2(16f, 16f), Amber);
            ringImage.sprite = UiSprites.Circle;
            card.Ring = new GameObject("Ring Anchor").transform;
            card.Ring.SetParent(card.Root.transform, false);
            ring.transform.SetParent(card.Ring, false);

            card.Line = card.Root.AddComponent<LineRenderer>();
            card.Line.positionCount = 2;
            card.Line.useWorldSpace = true;
            card.Line.numCapVertices = 2;
            if (_lineMaterial == null)
                _lineMaterial = RuntimeMaterials.Create(Amber);
            card.Line.sharedMaterial = _lineMaterial;
            card.Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            card.Line.receiveShadows = false;
            return card;
        }

        static Text MakeText(RectTransform parent, string name, string value, int size, FontStyle style, Color color)
        {
            var text = WorldUi.Label(parent, name, value ?? string.Empty, size, Vector2.zero, new Vector2(Width - Pad * 2f, 10f), style, TextAnchor.UpperLeft);
            text.color = color;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static void Place(Text text, float width, float top, float height)
        {
            var rect = text.rectTransform;
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(2f, top - height * 0.5f);
        }

        void OnDestroy()
        {
            if (_lineMaterial != null)
                Destroy(_lineMaterial);
        }
    }
}
