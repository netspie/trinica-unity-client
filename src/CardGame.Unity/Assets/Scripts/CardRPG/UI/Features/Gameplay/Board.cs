using CardRPG.UI.GUICommands;
using CardRPG.UI.Infrastructure;
using CardRPG.UI.UseCases;
using CardRPG.UseCases;
using Core.Basic;
using Core.Collections;
using Core.Functional;
using Core.Unity;
using Core.Unity.Math;
using Core.Unity.Scripts;
using Core.Unity.Transforms;
using Core.Unity.UI;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace CardRPG.UI.Gameplay
{
    public class Board : UnityScript
    {
        // Enemy
        [SerializeField] private RectTransform _enemyBackRow;
        [SerializeField] private RectTransform _enemyHandRow;
        [SerializeField] private RectTransform _enemyBattleRow;
        private Card _enemyHero;

        // Player
        [SerializeField] private RectTransform _playerBattleRow;
        [SerializeField] private RectTransform _playerHandRow;
        [SerializeField] private RectTransform _playerBackRow;
        private Card _playerHero;

        [SerializeField] private RectTransform _middleRow;

        [SerializeField] private PlayerActionController _playerActionController;

        [SerializeField] private Card _cardPrefab;
        [SerializeField] private Card _cardBigPrefab;
        private Card _cardBig;

        private Card _myDeck;
        private Card _enemyDeck;
        [SerializeField] private Card _commonDeck;

        private IGameplayService _gameplayService;

        private RectTransform _rt;

        [SerializeField] private Image _dialogTreeBg;
        private DialogTree _dialogTree;

        public void Init(GetGameStateQueryOut dto)
        {
            _rt = this.RT();

            _gameplayService = new OfflineGameplayService(StartCoroutine);
            _gameplayService.Subscribe<CardTakenToHandEvent>(On);

            _dialogTree = new(_rt, _dialogTreeBg, StartCoroutine);

            Rebuild(dto);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                _dialogTree.Any()
                    .IfTrueDo(_dialogTree.Back)
                    .IfFalseDo(FindObjectOfType<GoToMenuGUICommand>().Execute);
        }

        private void On(CardTakenToHandEvent ev)
        {
            TakeCardToHand(onDone: null, forEnemy: true, ev.IsFromCommonDeck);
        }

        public void Rebuild(GetGameStateQueryOut dto)
        {
            var steps = new ActionStepController();

            steps += Wait(0.5f);
            steps += SpawnHeroesAndDecks;
            steps += Show("Mixing Cards");
            steps += AnimateMixingCards;
            //steps += Wait(0.5f);
            steps += Show("Take Cards");
            steps += StartTakeCardsToHand;
            steps += Wait(0.5f);
            steps += Show("You Strike First", 1f);
            steps += Show("Lay the Cards");
            steps += StartLayingCardsToBattle;

            RunAsCoroutine(steps.Execute);
        }

        private void SpawnHeroesAndDecks(Action onDone)
        {
            // Heroes
            _playerHero = MoveInCard(_playerBackRow, isLeft: false, yOffset: 5);
            _enemyHero = MoveInCard(_enemyBackRow, isLeft: true, yOffset: -5);

            _playerHero.Turn(true);
            _playerHero.CardButton.OnTap(() => {
                if (_dialogTree.Any())
                    _dialogTree.Back();
                else
                    _dialogTree.ShowDialog(_cardBigPrefab.RT, _playerHero.RT.GetScreenCenterPos());
            });
            _enemyHero.Turn(true);

            // Decks
            _myDeck = MoveInCard(_playerBackRow, isLeft: true, yOffset: 5, onDone);
            _enemyDeck = MoveInCard(_enemyBackRow, isLeft: false, yOffset: -5);
        }

        private Card MoveInCard(RectTransform row, bool isLeft, float yOffset, Action onDone = null)
        {
            var card = _cardPrefab.Instantiate(row);

            var sign = isLeft ? -1 : 1;
            var xOffset = sign * (row.GetRTWidth() + card.RT.GetRTWidth()) / 2;
            var initPos = row.GetScreenCenterPos(xOffset, yOffset);
            card.RT.pivot = Vector2X.Half;
            card.RT.position = initPos;

            card.MoveTo(initPos.AddX(-sign * (card.RT.GetRTWidth() * card.RT.lossyScale.x + 10)), cardMoveTime: 0.75f, onDone: onDone);

            return card;
        }

        private void AnimateMixingCards(Action onDone)
        {
            var cardMoveTime = 0.75f;

            _myDeck.HideArrow();
            _enemyDeck.HideArrow();

            _myDeck.AnimateMixingCards(isMe: true, targetPos: _middleRow.GetScreenCenterPos(xOffset: -200), _commonDeck.RT, cardMoveTime);
            _enemyDeck.AnimateMixingCards(isMe: false, _middleRow.GetScreenCenterPos(xOffset: 200), _commonDeck.RT, cardMoveTime, 
                onDone: onDone);
        }

        private void StartTakeCardsToHand(Action onDone)
        {
            _myDeck.gameObject.SetActive(true);
            _enemyDeck.gameObject.SetActive(true);

            _commonDeck.gameObject.SetActive(true);
            _myDeck.ShowArrow();
            _commonDeck.ShowArrow();
            _enemyDeck.GrayOn();

            _myDeck.CardButton.OnSwipe(() => TakeCardToHand(onDone));
            _commonDeck.CardButton.OnSwipe(() => TakeCardToHand(onDone, fromCommonDeck: true));
        }

        public void TakeCardToHand(Action onDone, bool forEnemy = false, bool fromCommonDeck = false, float moveTime = 0.35f)
        {
            var row = forEnemy ? _enemyHandRow : _playerHandRow;

            var sourceCard = fromCommonDeck ? _commonDeck : (forEnemy ? _enemyDeck : _myDeck);
            var card = sourceCard.Instantiate();

            MoveCardToRow(card, row, Card.MoveEffect.Scale3D, moveTime, onDone: () =>
            {
                if (row.GetComponentsInChildren<Card>().Length == 6) 
                    (_myDeck + _commonDeck)
                        .ForEach(x => x.HideArrow().CardButton.RemoveHandlers())
                        .Then(onDone);
            });

            _gameplayService.Send(new TakeCardToHandCommand(!forEnemy));
        }

        private void StartLayingCardsToBattle(Action onDone)
        {
            _playerHandRow
                .GetChildren<Card>()
                .ForEach(card => card.CardButton
                    .Then(c => c.OnSwipe(() => LayCardToBattle(card, onDone: onDone))));
        }

        public void LayCardToBattle(Card card, bool forEnemy = false, float moveTime = 0.35f, Action onDone = null)
        {
            var row = forEnemy ? _enemyBattleRow : _playerBattleRow;
            MoveCardToRow(card, row, Card.MoveEffect.Scale2D, moveTime, onDone);
        }

        public void MoveCardToRow(
            Card card, RectTransform row, Card.MoveEffect effects, float moveTime = 0.35f,  Action onDone = null)
        { 
            var rowWidth = row.GetRTWidth();
            var cards = row.GetComponentsInChildren<Card>();
            var count = cards.Length;

            var spacing = 20;
            cards.ForEach((card, i) =>
            {
                var offsetFactor = (float) (i - (count + 1) / 2f + 0.5f);
                var xOffset = offsetFactor * (card.RT.rect.width + spacing);
                var rowPos = row.GetScreenCenterPos(xOffset);

                card.MoveTo(rowPos, moveTime);
            });

            var targetPos = row.RT().GetScreenCenterPos(xOffset: card.RT.rect.width * ((float) count / 2) + spacing * (count / 2f));
            card.RT.SetParent(row);
            card.CardButton.RemoveHandlers();
            UILayoutRebuilder.Rebuild(card.gameObject);
            card.GrayOff();
            card.MoveTo(targetPos, moveTime, effects);
            count++;

            if (count == 6)
                row
                   .GetChildren<Card>()
                   .ForEach(card => card.CardButton.RemoveHandlers())
                   .Then(onDone);
        }
    }
}
