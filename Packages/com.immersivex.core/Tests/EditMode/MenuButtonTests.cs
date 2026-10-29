using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ImmersiveX.Tests
{
    public class MenuButtonTests
    {
        static readonly Vector3 Eyes = new Vector3(0f, 1.6f, 0f);

        MenuButton _button;

        [TearDown]
        public void TearDown()
        {
            if (_button != null)
                Object.DestroyImmediate(_button.transform.root.gameObject);
        }

        [Test]
        public void ItIsAGridOfHalfTransparentWhiteSquares()
        {
            _button = MenuButton.Create(null, () => { });
            Assert.AreEqual(MenuButton.Grid * MenuButton.Grid, _button.Squares.Count);
            Assert.IsTrue(_button.Squares.All(s => s.color == new Color(1f, 1f, 1f, 0.5f)), "white at 50 %");
            Assert.IsTrue(_button.Squares.All(s => !s.raycastTarget), "the whole tile takes the click, not each square");
        }

        [Test]
        public void PressingIt_CallsItsAction()
        {
            var pressed = 0;
            _button = MenuButton.Create(null, () => pressed++);
            _button.Button.onClick.Invoke();
            Assert.AreEqual(1, pressed);
        }

        [Test]
        public void ItSitsInTheMiddleAtStomachLevel_BelowThePlayer_AndDoesntStack()
        {
            var follower = new WithinReach.Follower();
            follower.Step(Eyes, Vector3.forward, false, false, 0.016f);
            var button = follower.Place(MenuButton.Offset).position;
            var player = follower.Place(WithinReach.DefaultOffset).position;
            Assert.AreEqual(0f, button.x, 1e-4f, "in the middle");
            Assert.Greater(button.z, 0.25f, "in front");
            Assert.AreEqual(1.08f, button.y, 0.03f, "stomach level for 1.6 m eyes");
            Assert.Less(button.y, player.y - 0.1f, "below the player");

            _button = MenuButton.Create(null, () => { });
            Assert.IsFalse(_button.GetComponentInParent<WithinReach>().Stacks);
        }
    }
}
