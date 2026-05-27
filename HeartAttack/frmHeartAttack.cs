using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Media;
using System.Windows.Forms;

namespace HeartAttack
{
    public partial class frmHeartAttack : Form
    {
        // ── 遊戲狀態 ─────────────────────────────────────────────────────────────
        private HeartAttackGame game;
        private GamePhase phase = GamePhase.DifficultySelect;
        private string difficulty = "Medium";

        private List<int> slapOrder = new List<int>(); // 拍牌順序
        private bool slapActive = false;

        // 難度 → 反應時間範圍 (ms)
        private (int min, int max) GetRange()
        {
            switch (difficulty)
            {
                case "Easy": return (1500, 3200);
                case "Hard": return (130,  520);
                default:     return (650,  1600);
            }
        }

        // ── Timer ────────────────────────────────────────────────────────────────
        private readonly System.Windows.Forms.Timer aiFlipTimer     = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer ai1SlapTimer    = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer ai2SlapTimer    = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer slapTimeoutTimer     = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer nextTurnTimer        = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer flashTimer           = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer humanAutoFlipTimer   = new System.Windows.Forms.Timer { Interval = 5000 };

        // ── UI 狀態 ───────────────────────────────────────────────────────────────
        private bool flashOn = false;
        private readonly Random rng = new Random();

        // ── 控制項 ───────────────────────────────────────────────────────────────
        private DoubleBufferedPanel gamePanel;
        private Panel               diffPanel;
        private Button btnFlip;
        private Button btnSlap;
        private Label  lblMsg;

        // ── 音效 ─────────────────────────────────────────────────────────────────
        // ── 音效 ─────────────────────────────────────────────────────────────


        // ── 牌的尺寸（配合圖片 85x115）────────────────────────────────────────────
        private const int CW = 85, CH = 115;

        // ── 牌面圖片快取 ──────────────────────────────────────────────────────────
        // cardImages[1..52]：對應 pic1.png ~ pic52.png
        // 對應規則：picIndex = (rank-1)*4 + suitIndex + 1
        //   ♣=0, ♦=1, ♥=2, ♠=3
        private readonly Image[] cardImages  = new Image[53];
        private Image cardBackImage;
        private bool  imagesLoaded = false;

        // =====================================================================
        // 初始化
        // =====================================================================
        public frmHeartAttack()
        {
            InitializeComponent();
            BuildUI();
            LoadCardImages();   // 先載入圖片
            LoadSounds();
            SetupTimers();
        }

        private void BuildUI()
        {
            Text            = "♥  心臟病  Heart Attack  ♥";
            ClientSize      = new Size(820, 650);
            BackColor       = Color.FromArgb(12, 85, 28);
            DoubleBuffered  = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox     = false;
            KeyPreview      = true;
            KeyDown        += OnKeyDown;

            // ── 遊戲繪圖面板 ──
            gamePanel = new DoubleBufferedPanel
            {
                Location  = Point.Empty,
                Size      = new Size(820, 600),
                BackColor = Color.Transparent
            };
            gamePanel.Paint      += PaintGame;
            gamePanel.MouseClick += OnPanelClick;
            Controls.Add(gamePanel);

            // ── 底部訊息列 ──
            lblMsg = new Label
            {
                Location  = new Point(0, 604),
                Size      = new Size(820, 46),
                BackColor = Color.FromArgb(0, 55, 15),
                ForeColor = Color.White,
                Font      = new Font("微軟正黑體", 12),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(lblMsg);

            // ── 出牌按鈕（加入 gamePanel，避免被蓋住）──
            btnFlip = new Button
            {
                Text      = "出  牌",
                Font      = new Font("微軟正黑體", 14, FontStyle.Bold),
                Size      = new Size(130, 50),
                Location  = new Point(270, 540),
                BackColor = Color.FromArgb(28, 130, 230),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
                Visible   = true,
                Enabled   = false
            };
            btnFlip.FlatAppearance.BorderSize = 0;
            btnFlip.Click += OnFlipClick;
            gamePanel.Controls.Add(btnFlip);

            // ── 拍！按鈕（出牌旁邊，全程顯示）──
            btnSlap = new Button
            {
                Text      = "拍 ！ 👋",
                Font      = new Font("微軟正黑體", 14, FontStyle.Bold),
                Size      = new Size(130, 50),
                Location  = new Point(420, 540),
                BackColor = Color.Crimson,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor    = Cursors.Hand,
                Visible   = false
            };
            btnSlap.FlatAppearance.BorderSize = 0;
            btnSlap.Click += OnSlapClick;
            gamePanel.Controls.Add(btnSlap);

            // ── 難度選擇面板 ──
            BuildDiffPanel();
            ShowDiffPanel();
        }

        private void BuildDiffPanel()
        {
            diffPanel = new Panel
            {
                Size      = new Size(440, 380),
                BackColor = Color.FromArgb(248, 8, 45, 18)
            };
            diffPanel.Location = new Point(190, 130);

            var title = new Label
            {
                Text      = "♥  心臟病  Heart Attack  ♥",
                Font      = new Font("微軟正黑體", 19, FontStyle.Bold),
                ForeColor = Color.FromArgb(255, 218, 70),
                TextAlign = ContentAlignment.MiddleCenter,
                Size      = new Size(440, 52),
                Location  = new Point(0, 18)
            };

            var sub = new Label
            {
                Text      = "3 人遊玩（你 + 電腦 A + 電腦 B）\n請選擇電腦難度",
                Font      = new Font("微軟正黑體", 12),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                Size      = new Size(440, 52),
                Location  = new Point(0, 72)
            };

            // 三個難度按鈕
            var configs = new[]
            {
                new { Label = "😊  簡單（Easy）",   Key = "Easy",   Clr = Color.FromArgb(65, 155, 65),
                      Desc = "電腦反應時間：1.5 ～ 3.2 秒" },
                new { Label = "😐  普通（Medium）", Key = "Medium", Clr = Color.FromArgb(195, 135, 18),
                      Desc = "電腦反應時間：0.65 ～ 1.6 秒" },
                new { Label = "😤  困難（Hard）",   Key = "Hard",   Clr = Color.FromArgb(185, 45, 45),
                      Desc = "電腦反應時間：0.13 ～ 0.52 秒" },
            };

            for (int i = 0; i < configs.Length; i++)
            {
                var cfg = configs[i];
                var btn = new Button
                {
                    Text      = cfg.Label,
                    Font      = new Font("微軟正黑體", 13, FontStyle.Bold),
                    Size      = new Size(250, 52),
                    Location  = new Point(95, 140 + i * 72),
                    BackColor = cfg.Clr,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Cursor    = Cursors.Hand,
                    Tag       = cfg.Key
                };
                btn.FlatAppearance.BorderSize = 0;
                btn.Click += OnDiffClick;

                var lbl = new Label
                {
                    Text      = cfg.Desc,
                    Font      = new Font("微軟正黑體", 9),
                    ForeColor = Color.FromArgb(185, 255, 255, 255),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Size      = new Size(440, 18),
                    Location  = new Point(0, 165 + i * 72)
                };

                diffPanel.Controls.Add(btn);
                diffPanel.Controls.Add(lbl);
            }

            diffPanel.Controls.Add(title);
            diffPanel.Controls.Add(sub);
            Controls.Add(diffPanel);
            diffPanel.BringToFront();
        }

        private void SetupTimers()
        {
            flashTimer.Interval = 175;

            aiFlipTimer.Tick         += (s, e) => { aiFlipTimer.Stop();         DoFlip(); };
            ai1SlapTimer.Tick        += (s, e) => { ai1SlapTimer.Stop();        RegisterSlap(1); };
            ai2SlapTimer.Tick        += (s, e) => { ai2SlapTimer.Stop();        RegisterSlap(2); };
            humanAutoFlipTimer.Tick  += (s, e) => { humanAutoFlipTimer.Stop();  DoFlip(); }; // 5秒自動出牌
            slapTimeoutTimer.Tick    += OnSlapTimeout;
            nextTurnTimer.Tick       += OnNextTurnNormal;
            flashTimer.Tick          += (s, e) => { flashOn = !flashOn; gamePanel.Invalidate(); };
        }

        // =====================================================================
        // 音效
        // =====================================================================
        // =====================================================================
        // 圖片載入
        // =====================================================================
        private void LoadCardImages()
        {
            string resDir = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Resources");

            if (!System.IO.Directory.Exists(resDir)) return;

            try
            {
                for (int i = 1; i <= 52; i++)
                {
                    string path = System.IO.Path.Combine(resDir, $"pic{i}.png");
                    if (System.IO.File.Exists(path))
                        cardImages[i] = Image.FromFile(path);
                }
                string backPath = System.IO.Path.Combine(resDir, "back.png");
                if (System.IO.File.Exists(backPath))
                    cardBackImage = Image.FromFile(backPath);

                imagesLoaded = (cardImages[1] != null);
            }
            catch { imagesLoaded = false; }
        }

        // 根據花色與點數計算 pic 編號
        // picNum = (rank-1)*4 + suitIndex + 1
        private int GetPicIndex(Card c)
            => (c.Rank - 1) * 4 + (int)c.Suit + 1;

        // =====================================================================
        // 音效
        // =====================================================================
        // ── 音效 ─────────────────────────────────────────────────────────────
        private string _soundDir = null;

        private void LoadSounds()
        {
            string exeDir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            string[] candidates = {
                System.IO.Path.Combine(exeDir, "Sounds"),                        // bin\Debug\Sounds
                System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, @"..\..\Sounds")),  // 專案根目錄\Sounds
                System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, @"..\Sounds")),     // 上一層
                System.IO.Path.Combine(Application.StartupPath, "Sounds"),
                "Sounds"
            };
            foreach (var d in candidates)
                if (System.IO.Directory.Exists(d)) { _soundDir = d; break; }
        }

        // 翻牌時播對應數字音效
        private void PlayNum(int rank)
        {
            string[] names = { "A","2","3","4","5","6","7","8","9","10","J","Q","K" };
            PlayFile(names[rank - 1] + ".wav");
        }

        // 播指定音效檔
        private void PlayFile(string filename)
        {
            if (_soundDir == null) return;
            string path = System.IO.Path.Combine(_soundDir, filename);
            if (!System.IO.File.Exists(path)) return;
            try { new System.Media.SoundPlayer(path).Play(); }
            catch { }
        }


        // =====================================================================
        // 難度選擇
        // =====================================================================
        private void ShowDiffPanel()
        {
            StopAll();
            diffPanel.Visible = true;
            diffPanel.BringToFront();
            btnFlip.Visible = false;   // 難度選擇畫面隱藏出牌
            btnSlap.Visible = false;
            phase = GamePhase.DifficultySelect;
        }

        private void OnDiffClick(object sender, EventArgs e)
        {
            difficulty = (string)((Button)sender).Tag;
            diffPanel.Visible = false;
            StartNewGame();
        }

        // =====================================================================
        // 遊戲流程
        // =====================================================================
        private void StartNewGame()
        {
            StopAll();
            game = new HeartAttackGame();
            slapOrder.Clear();
            slapActive = false;
            flashOn    = false;
            foreach (var p in game.Players) p.JustSlapped = false;

            btnFlip.Visible = true;    // 遊戲開始後全程顯示
            btnFlip.Enabled = false;   // 預設不能按，輪到玩家才開放
            btnSlap.Visible = true;
            btnSlap.BringToFront();
            SetMsg("遊戲開始！");
            gamePanel.Invalidate();

            nextTurnTimer.Interval = 900;
            nextTurnTimer.Start();
        }

        private void StartTurn()
        {
            if (game == null) return;
            if (game.IsGameOver) { EndGame(); return; }

            var cur = game.Players[game.CurrentPlayerIdx];

            // 牌出完的玩家跳過（不參與數數），但仍可拍牌
            if (cur.NeedsExtraSlap)
            {
                game.AdvanceTurn();
                nextTurnTimer.Interval = 150;
                nextTurnTimer.Start();
                return;
            }

            // 安全檢查：若所有存活玩家都無牌，直接平手
            if (game.Players.Where(p => p.IsAlive).All(p => p.NeedsExtraSlap))
            {
                DrawGame();
                return;
            }

            phase = GamePhase.WaitingForFlip;
            string num = game.CurrentNumberString;

            if (cur.Type == PlayerType.Human)
            {
                btnFlip.Enabled = true;
                SetMsg($"你的回合 → 喊「{num}」，按「出牌」（5秒未出牌自動翻）");
                humanAutoFlipTimer.Stop();
                humanAutoFlipTimer.Start();   // 5秒未出牌，自動翻
            }
            else
            {
                btnFlip.Enabled = false;
                SetMsg($"{cur.Name} 的回合（喊 {num}）...");
                aiFlipTimer.Interval = 380 + rng.Next(680);
                aiFlipTimer.Start();
            }

            gamePanel.Invalidate();
        }

        // ── 翻牌 ──────────────────────────────────────────────────────────────
        private void OnFlipClick(object sender, EventArgs e)
        {
            if (phase != GamePhase.WaitingForFlip) return;
            humanAutoFlipTimer.Stop();   // 手動出牌，取消自動計時
            btnFlip.Enabled = false;
            DoFlip();
        }

        private void DoFlip()
        {
            if (game == null) return;

            int flipperIdx = game.CurrentPlayerIdx;
            var result     = game.FlipCard();
            var card       = result.card;
            var isMatch    = result.isMatch;
            var calledNum  = result.calledNumber;

            PlayNum(calledNum);

            // 翻牌後檢查：牌剛出完 → 設定旗標，需再拍對一次才能退出
            var flipper = game.Players[flipperIdx];
            if (card != null && flipper.Deck.Count == 0 && !flipper.NeedsExtraSlap)
            {
                flipper.NeedsExtraSlap = true;
                SetMsg(string.Format("🃏 {0} 牌出完！需再拍對一次才能退出！", flipper.Name));
            }

            gamePanel.Invalidate();

            if (isMatch)
                BeginSlapPhase();
            else
            {
                game.AdvanceTurn();
                nextTurnTimer.Interval = (card == null) ? 300 : 650;
                nextTurnTimer.Start();
            }
        }

        // ── 搶拍階段 ──────────────────────────────────────────────────────────
        private void BeginSlapPhase()
        {
            phase = GamePhase.SlapPhase;
            slapActive = true;
            slapOrder.Clear();
            foreach (var p in game.Players) p.JustSlapped = false;

            SetMsg("🔥🔥  數字相同！快拍！！（點「拍！」按鈕）🔥🔥");

            btnSlap.Enabled = true;
            btnSlap.BringToFront();
            btnFlip.Enabled = false;
            flashTimer.Start();

            // 設定電腦反應時間（隨機區間）
            var range = GetRange();
            if (game.Players[1].IsAlive)
            {
                ai1SlapTimer.Interval = range.min + rng.Next(range.max - range.min);
                ai1SlapTimer.Start();
            }
            if (game.Players[2].IsAlive)
            {
                // 讓兩台電腦反應時間略有差異，更真實
                int extra = rng.Next(-(range.max - range.min) / 4, (range.max - range.min) / 4);
                int t2 = Math.Max(range.min, Math.Min(range.max,
                         range.min + rng.Next(range.max - range.min) + extra));
                ai2SlapTimer.Interval = t2;
                ai2SlapTimer.Start();
            }

            // 整體 Timeout（人類沒拍視為最後）
            slapTimeoutTimer.Interval = range.max + 1800;
            slapTimeoutTimer.Start();

            gamePanel.Invalidate();
        }

        private void OnSlapClick(object sender, EventArgs e)
        {
            if (phase == GamePhase.DifficultySelect || phase == GamePhase.GameOver) return;
            if (!game.Players[0].IsAlive) return;

            if (slapActive)
            {
                // ✅ 正確拍牌：加入搶拍順序
                if (!slapOrder.Contains(0))
                    RegisterSlap(0);
            }
            else
            {
                // ❌ 搶拍失敗：牌堆歸玩家（扣罰）
                if (game.Pile.Count == 0)
                {
                    SetMsg("⚠️ 搶拍失敗！牌堆是空的，沒有牌可以收。");
                    return;
                }

                int pileCount = game.Pile.Count;
                game.GivePileTo(0);   // 牌堆全部給玩家
                game.SetTurn(0);      // 玩家收牌後先出
                PlayFile("slap.wav");
                SetMsg(string.Format("❌ 搶拍失敗！你收走了 {0} 張牌作為懲罰！", pileCount));
                gamePanel.Invalidate();
            }
        }

        private void RegisterSlap(int playerIdx)
        {
            if (!slapActive) return;
            if (slapOrder.Contains(playerIdx)) return;
            if (!game.Players[playerIdx].IsAlive) return;

            slapOrder.Add(playerIdx);
            game.Players[playerIdx].JustSlapped = true;
            PlayFile("slap.wav");
            gamePanel.Invalidate();

            if (slapOrder.Count >= game.AliveCount)
                ResolveSlap();
        }

        private void OnSlapTimeout(object sender, EventArgs e)
        {
            slapTimeoutTimer.Stop();
            if (!slapActive) return;

            // 把還沒拍的存活玩家全部加入（視為最後）
            for (int i = 0; i < game.Players.Length; i++)
                if (game.Players[i].IsAlive && !slapOrder.Contains(i))
                    slapOrder.Add(i);

            ResolveSlap();
        }

        private void ResolveSlap()
        {
            slapActive = false;
            StopSlapTimers();
            flashTimer.Stop();
            flashOn = false;
            // 拍！按鈕繼續顯示

            if (slapOrder.Count == 0) { StartTurn(); return; }

            int loserIdx  = slapOrder[slapOrder.Count - 1];
            int pileCount = game.Pile.Count;
            game.GivePileTo(loserIdx);
            game.Players[loserIdx].NeedsExtraSlap = false; // 收到牌，重新計算

            string msg = string.Format("{0} 最後拍到，收走 {1} 張牌！",
                game.Players[loserIdx].Name, pileCount);

            // 檢查：NeedsExtraSlap 且非最後 → 成功退出
            bool humanWon = false;
            for (int i = 0; i < game.Players.Length; i++)
            {
                var p = game.Players[i];
                if (!p.IsAlive || i == loserIdx || !p.NeedsExtraSlap) continue;
                p.IsAlive = false;
                p.NeedsExtraSlap = false;
                msg += string.Format("   🏆 {0} 成功退出遊戲！", p.Name);
                if (p.Id == 0) humanWon = true;
            }

            SetMsg(msg);
            foreach (var p in game.Players) p.JustSlapped = false;
            phase = GamePhase.ShowingResult;
            gamePanel.Invalidate();

            // 玩家是第一個成功退出 → 立刻宣告獲勝
            if (humanWon)
            {
                nextTurnTimer.Interval = 1500;
                nextTurnTimer.Tick -= OnNextTurnNormal; // 避免重複訂閱
                nextTurnTimer.Tick += OnNextTurnHumanWin;
                nextTurnTimer.Start();
                return;
            }

            if (game.IsGameOver)
                nextTurnTimer.Interval = 2000;
            else
            {
                game.SetTurn(loserIdx);
                nextTurnTimer.Interval = 1600;
            }
            nextTurnTimer.Start();
        }

        // ── 平手 ──────────────────────────────────────────────────────────────
        private void DrawGame()
        {
            phase = GamePhase.GameOver;
            StopAll();

            SetMsg("🤝  平手！所有人牌都出完了！");
            btnFlip.Visible = false;
            btnSlap.Visible = false;
            gamePanel.Invalidate();

            var t = new System.Windows.Forms.Timer { Interval = 2000 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                MessageBox.Show("🤝 平手！\n所有人牌都出完了，無法繼續。",
                    "遊戲結束", MessageBoxButtons.OK, MessageBoxIcon.Information);
                ShowDiffPanel();
            };
            t.Start();
        }

        // ── 遊戲結束 ──────────────────────────────────────────────────────────
        private void EndGame(bool forceHumanWin = false)
        {
            phase = GamePhase.GameOver;
            StopAll();

            bool humanLost;
            if (forceHumanWin)
            {
                humanLost = false;
                PlayFile("win.wav");
                SetMsg("🎉  恭喜！你贏了！");
            }
            else
            {
                var loser = game.GetLoser();
                humanLost = loser != null && loser.Id == 0;
                if (humanLost) { PlayFile("lose.wav"); SetMsg("😢  你輸了！電腦獲勝！"); }
                else           { PlayFile("win.wav");  SetMsg("🎉  恭喜！你贏了！");     }
            }

            btnFlip.Visible = false;
            btnSlap.Visible = false;
            gamePanel.Invalidate();

            var t = new System.Windows.Forms.Timer { Interval = 2500 };
            t.Tick += (s, e) =>
            {
                t.Stop();
                string msg = humanLost
                    ? "😢 你輸了！\n要重新選擇難度嗎？"
                    : "🎉 你贏了！\n要重新選擇難度嗎？";
                var icon = humanLost ? MessageBoxIcon.Error : MessageBoxIcon.Information;
                var res  = MessageBox.Show(msg, "遊戲結束", MessageBoxButtons.YesNo, icon);
                if (res == DialogResult.Yes) ShowDiffPanel();
            };
            t.Start();
        }

        // ── Timer 工具 ────────────────────────────────────────────────────────
        private void OnNextTurnNormal(object sender, EventArgs e)
        {
            nextTurnTimer.Stop();
            nextTurnTimer.Tick -= OnNextTurnHumanWin; // 確保清除人類獲勝 handler
            StartTurn();
        }

        private void OnNextTurnHumanWin(object sender, EventArgs e)
        {
            nextTurnTimer.Stop();
            nextTurnTimer.Tick -= OnNextTurnHumanWin;
            nextTurnTimer.Tick += OnNextTurnNormal;
            EndGame(forceHumanWin: true);
        }

        private void StopSlapTimers()
        {
            ai1SlapTimer.Stop();
            ai2SlapTimer.Stop();
            slapTimeoutTimer.Stop();
        }

        private void StopAll()
        {
            aiFlipTimer.Stop();
            humanAutoFlipTimer.Stop();
            StopSlapTimers();
            nextTurnTimer.Stop();
            flashTimer.Stop();
        }

        private void SetMsg(string s) { lblMsg.Text = s; }

        // ── 鍵盤 ──────────────────────────────────────────────────────────────
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Space → 拍（正確或搶拍失敗）
            if (e.KeyCode == Keys.Space)
            {
                OnSlapClick(null, null);
                e.Handled = true;
            }
            // Enter / F → 出牌
            else if ((e.KeyCode == Keys.Return || e.KeyCode == Keys.F)
                     && phase == GamePhase.WaitingForFlip
                     && btnFlip.Enabled)
            {
                OnFlipClick(null, null);
                e.Handled = true;
            }
        }

        // 點擊中央牌堆區域也可拍
        private void OnPanelClick(object sender, MouseEventArgs e)
        {
            var pileZone = new Rectangle(265, 135, 180, CH + 30);
            if (slapActive && !slapOrder.Contains(0) && pileZone.Contains(e.Location))
                OnSlapClick(null, null);
        }

        // =====================================================================
        // 繪圖
        // =====================================================================
        private void PaintGame(object sender, PaintEventArgs e)
        {
            if (phase == GamePhase.DifficultySelect) return;
            if (game == null) return;

            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            DrawBackground(g);

            // 三個玩家區域
            DrawPlayerZone(g, 1, new Rectangle(12,  48, 215, 205));   // 電腦A 左上
            DrawPlayerZone(g, 2, new Rectangle(593, 48, 215, 205));   // 電腦B 右上
            DrawPlayerZone(g, 0, new Rectangle(165, 395, 490, 165));  // 玩家 下方

            // 中央牌堆
            DrawCenterZone(g);

            // 輸贏遮罩
            if (phase == GamePhase.GameOver)
                DrawGameOver(g);
        }

        private void DrawBackground(Graphics g)
        {
            using (var br = new LinearGradientBrush(
                new Rectangle(0, 0, 820, 600),
                Color.FromArgb(12, 85, 28), Color.FromArgb(4, 55, 15),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(br, 0, 0, 820, 600);
            }
        }

        private void DrawPlayerZone(Graphics g, int idx, Rectangle r)
        {
            var p      = game.Players[idx];
            bool isHuman = idx == 0;
            bool isTurn  = (phase == GamePhase.WaitingForFlip && game.CurrentPlayerIdx == idx && p.IsAlive);

            Color bgColor  = p.IsAlive
                ? (isHuman ? Color.FromArgb(22, 75, 195, 75) : Color.FromArgb(22, 170, 170, 245))
                : Color.FromArgb(22, 75, 75, 75);
            Color borColor = p.IsAlive
                ? (isHuman ? Color.FromArgb(115, 75, 215, 75) : Color.FromArgb(115, 150, 150, 245))
                : Color.FromArgb(55, 110, 110, 110);

            var path = RoundRect(r, 12);
            using (var br = new SolidBrush(bgColor)) g.FillPath(br, path);

            // 當前回合金框
            if (isTurn)
            {
                using (var glow = new Pen(Color.FromArgb(180, 255, 215, 0), 3f))
                    g.DrawPath(glow, path);
            }
            else
            {
                using (var pen = new Pen(borColor, 1.5f))
                    g.DrawPath(pen, path);
            }

            // 玩家名稱 + 牌數
            string icon   = isHuman ? "👤" : "🤖";
            string status = p.IsAlive ? string.Format("{0} 張", p.Deck.Count) : "✅ 已退出";
            Color nameClr = p.IsAlive
                ? (isHuman ? Color.FromArgb(110, 255, 110) : Color.FromArgb(195, 215, 255))
                : Color.FromArgb(130, 130, 130);

            using (var f = new Font("微軟正黑體", 11, FontStyle.Bold))
            using (var b = new SolidBrush(nameClr))
                g.DrawString(string.Format("{0} {1}  {2}", icon, p.Name, status), f, b, r.X + 8, r.Y + 7);

            // 牌背堆疊
            if (p.Deck.Count > 0)
            {
                int stk = Math.Min(8, p.Deck.Count);
                for (int i = stk - 1; i >= 0; i--)
                    DrawCardBack(g, r.X + 12 + i * 3, r.Y + 33 + (stk - 1 - i) * 2);
            }
            else if (p.IsAlive)
            {
                using (var f = new Font("微軟正黑體", 9))
                using (var b = new SolidBrush(Color.FromArgb(155, 255, 255, 255)))
                    g.DrawString("（牌出完）\n等待拍牌退出", f, b, r.X + 12, r.Y + 50);
            }

            // 拍！提示
            if (p.JustSlapped)
            {
                using (var f = new Font("微軟正黑體", 13, FontStyle.Bold))
                using (var b = new SolidBrush(Color.Yellow))
                    g.DrawString("👋 拍！", f, b, r.Right - 75, r.Bottom - 34);
            }
        }

        private void DrawCenterZone(Graphics g)
        {
            int cx   = 410;
            int pileX = cx - CW / 2 - 58;
            int pileY = 138;

            // 標題
            using (var f  = new Font("微軟正黑體", 10, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromArgb(175, 255, 255, 255)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString(string.Format("中央牌堆 ({0} 張)", game.Pile.Count),
                    f, br, new RectangleF(cx - 100, pileY - 24, 200, 22), sf);
            }

            // 搶拍閃爍光暈
            if (slapActive && flashOn)
            {
                using (var glow = new SolidBrush(Color.FromArgb(55, 255, 50, 50)))
                    g.FillEllipse(glow, cx - 115, pileY - 28, 230, CH + 58);
            }

            // 堆疊牌背
            if (game.Pile.Count > 1)
            {
                int behind = Math.Min(5, game.Pile.Count - 1);
                for (int i = behind; i >= 1; i--)
                    DrawCardBack(g, pileX - i * 3, pileY + i * 3);
            }

            // 最上面的牌（翻開）
            if (game.LastFlippedCard != null)
                DrawCard(g, game.LastFlippedCard, pileX, pileY, slapActive && flashOn);
            else
                DrawEmptySlot(g, pileX, pileY);

            // 喊數字框
            DrawNumberBox(g, cx + 58, pileY);

            // 拍牌順序
            if (slapOrder.Count > 0)
                DrawSlapOrder(g, cx, pileY + CH + 18);
        }

        private void DrawNumberBox(Graphics g, int x, int y)
        {
            var r = new Rectangle(x, y, 108, CH);
            using (var br = new SolidBrush(Color.FromArgb(205, 8, 8, 8)))
                FillRR(g, br, r, 10);

            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Near
            };

            // 標籤：剛才喊的
            using (var f  = new Font("微軟正黑體", 9))
            using (var br = new SolidBrush(Color.FromArgb(155, 255, 255, 255)))
                g.DrawString("剛才喊的", f, br, new RectangleF(x, y + 7, 108, 20), sf);

            // 數字：顯示 LastCalledNumber（翻牌後才更新）
            string displayNum = game.LastCalledNumber == 0 ? "—" : game.LastCalledString;
            Color numClr = slapActive ? Color.Crimson : Color.FromArgb(255, 215, 70);
            using (var f  = new Font("微軟正黑體", 28, FontStyle.Bold))
            using (var br = new SolidBrush(numClr))
            {
                var sfC = new StringFormat
                {
                    Alignment     = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(displayNum, f, br,
                    new RectangleF(x, y + 22, 108, 58), sfC);
            }

            // 下一張提示
            if (!slapActive)
            {
                using (var f  = new Font("微軟正黑體", 9))
                using (var br = new SolidBrush(Color.FromArgb(130, 255, 255, 255)))
                {
                    var sfC = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString($"下一張：{game.CurrentNumberString}", f, br,
                        new RectangleF(x, y + 82, 108, 20), sfC);
                }
            }

            // 相符提示
            if (slapActive)
            {
                using (var f  = new Font("微軟正黑體", 9, FontStyle.Bold))
                using (var br = new SolidBrush(Color.FromArgb(flashOn ? 255 : 160, 255, 80, 80)))
                {
                    var sfC = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString("⚡ 相符！⚡", f, br, new RectangleF(x, y + 85, 108, 20), sfC);
                }
            }
        }

        private void DrawSlapOrder(Graphics g, int cx, int y)
        {
            using (var f  = new Font("微軟正黑體", 9))
            using (var br = new SolidBrush(Color.FromArgb(190, 255, 255, 255)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                g.DrawString("拍牌順序：", f, br, new RectangleF(cx - 85, y, 170, 18), sf);
            }

            for (int i = 0; i < slapOrder.Count; i++)
            {
                int pi   = slapOrder[i];
                bool last = i == slapOrder.Count - 1 && slapOrder.Count == game.AliveCount;
                Color c  = last ? Color.Tomato : Color.LightGreen;
                using (var f  = new Font("微軟正黑體", 9))
                using (var br = new SolidBrush(c))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center };
                    string label = last ? string.Format("({0}. {1} ← 收牌！)", i + 1, game.Players[pi].Name)
                                       : string.Format("{0}. {1}", i + 1, game.Players[pi].Name);
                    g.DrawString(label, f, br, new RectangleF(cx - 85, y + 18 + i * 17, 170, 17), sf);
                }
            }
        }

        private void DrawGameOver(Graphics g)
        {
            using (var ov = new SolidBrush(Color.FromArgb(165, 0, 0, 0)))
                g.FillRectangle(ov, 0, 0, 820, 600);

            var loser     = game.GetLoser();
            bool humanLost = loser != null && loser.Id == 0;
            string line1  = humanLost ? "😢  你  輸  了  ！" : "🎉  恭  喜  獲  勝  ！";
            string line2  = loser != null
                ? string.Format("輸家：{0}", loser.Name)
                : "遊戲結束";

            var sfC = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            using (var f  = new Font("微軟正黑體", 30, FontStyle.Bold))
            using (var br = new SolidBrush(humanLost ? Color.Tomato : Color.Gold))
                g.DrawString(line1, f, br, new RectangleF(0, 190, 820, 100), sfC);

            using (var f  = new Font("微軟正黑體", 16))
            using (var br = new SolidBrush(Color.White))
                g.DrawString(line2, f, br, new RectangleF(0, 295, 820, 50), sfC);
        }

        // =====================================================================
        // 牌的繪製工具
        // =====================================================================
        private void DrawCard(Graphics g, Card c, int x, int y, bool highlight = false)
        {
            var r = new Rectangle(x, y, CW, CH);

            // 陰影
            using (var sh = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                g.FillRectangle(sh, x + 4, y + 4, CW, CH);

            int picIdx = GetPicIndex(c);
            if (imagesLoaded && cardImages[picIdx] != null)
            {
                // 使用圖片繪製（高品質縮放）
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(cardImages[picIdx], r);
            }
            else
            {
                // Fallback：GDI+ 手繪
                using (var bg = new SolidBrush(Color.White))
                    FillRR(g, bg, r, 7);
                using (var cf = new Font("Arial", 12, FontStyle.Bold))
                using (var cb = new SolidBrush(c.SuitColor))
                {
                    g.DrawString(c.RankString, cf, cb, x + 4, y + 3);
                    g.DrawString(c.SuitSymbol, cf, cb, x + 4, y + 19);
                }
                var sfC = new StringFormat
                {
                    Alignment     = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                using (var sf = new Font("Segoe UI Symbol", 26))
                using (var sb = new SolidBrush(c.SuitColor))
                    g.DrawString(c.SuitSymbol, sf, sb, r, sfC);
            }

            // 搶拍時金框高亮
            if (highlight)
            {
                using (var pen = new Pen(Color.Gold, 3f))
                    g.DrawPath(pen, RoundRect(r, 7));
            }
        }

        private void DrawCardBack(Graphics g, int x, int y)
        {
            var r = new Rectangle(x, y, CW, CH);

            // 陰影
            using (var sh = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
                g.FillRectangle(sh, x + 4, y + 4, CW, CH);

            if (imagesLoaded && cardBackImage != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(cardBackImage, r);
            }
            else
            {
                // Fallback：GDI+ 手繪藍色牌背
                using (var bg = new SolidBrush(Color.FromArgb(25, 72, 175)))
                    FillRR(g, bg, r, 7);
                using (var ip = new Pen(Color.White, 1.5f))
                    g.DrawPath(ip, RoundRect(new Rectangle(x + 6, y + 6, CW - 12, CH - 12), 4));
                using (var pp = new Pen(Color.FromArgb(32, 255, 255, 255), 1))
                    for (int i = x + 10; i < x + CW - 10; i += 8)
                        g.DrawLine(pp, i, y + 10, i, y + CH - 10);
            }
        }

        private void DrawEmptySlot(Graphics g, int x, int y)
        {
            var r = new Rectangle(x, y, CW, CH);
            using (var pen = new Pen(Color.FromArgb(65, 255, 255, 255), 2))
                g.DrawPath(pen, RoundRect(r, 7));
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            using (var f  = new Font("微軟正黑體", 10))
            using (var br = new SolidBrush(Color.FromArgb(75, 255, 255, 255)))
                g.DrawString("牌堆空了", f, br, r, sf);
        }

        private void FillRR(Graphics g, Brush br, Rectangle r, int rad)
            => g.FillPath(br, RoundRect(r, rad));

        private GraphicsPath RoundRect(Rectangle b, int rad)
        {
            var gp = new GraphicsPath();
            gp.AddArc(b.X,              b.Y,               rad * 2, rad * 2, 180, 90);
            gp.AddArc(b.Right - rad*2,  b.Y,               rad * 2, rad * 2, 270, 90);
            gp.AddArc(b.Right - rad*2,  b.Bottom - rad*2,  rad * 2, rad * 2,   0, 90);
            gp.AddArc(b.X,              b.Bottom - rad*2,  rad * 2, rad * 2,  90, 90);
            gp.CloseFigure();
            return gp;
        }
    }

    // =========================================================
    // 以下為遊戲邏輯類別（Card / Player / HeartAttackGame 等）
    // Card.cs 合併於此
    // =========================================================================
    public enum Suit { Clubs = 0, Diamonds = 1, Hearts = 2, Spades = 3 }

    public class Card
    {
        public Suit Suit { get; }
        public int Rank { get; }   // 1=A, 11=J, 12=Q, 13=K

        public Card(Suit suit, int rank) { Suit = suit; Rank = rank; }

        public bool IsRed => Suit == Suit.Diamonds || Suit == Suit.Hearts;

        public string RankString
        {
            get
            {
                switch (Rank)
                {
                    case 1:  return "A";
                    case 11: return "J";
                    case 12: return "Q";
                    case 13: return "K";
                    default: return Rank.ToString();
                }
            }
        }

        public string SuitSymbol
        {
            get
            {
                switch (Suit)
                {
                    case Suit.Clubs:    return "♣";
                    case Suit.Diamonds: return "♦";
                    case Suit.Hearts:   return "♥";
                    case Suit.Spades:   return "♠";
                    default:            return "";
                }
            }
        }

        public Color SuitColor => IsRed ? Color.Crimson : Color.FromArgb(20, 20, 20);
    }

    // =========================================================================
    // HeartAttackGame.cs 合併於此
    // =========================================================================
    public enum PlayerType { Human, Computer }

    public class Player
    {
        public int Id { get; }
        public string Name { get; }
        public PlayerType Type { get; }
        public Queue<Card> Deck { get; } = new Queue<Card>();
        public bool IsAlive { get; set; } = true;
        public bool JustSlapped { get; set; } = false;
        public bool NeedsExtraSlap { get; set; } = false; // 牌出完後還需拍對一次

        public Player(int id, string name, PlayerType type)
        {
            Id = id; Name = name; Type = type;
        }
    }

    public class HeartAttackGame
    {
        public Player[] Players { get; }
        public List<Card> Pile { get; } = new List<Card>();

        public int CurrentNumber { get; private set; } = 1;
        public int LastCalledNumber { get; private set; } = 0;
        public Card LastFlippedCard { get; private set; }
        public int CurrentPlayerIdx { get; private set; } = 0;

        public string CurrentNumberString => NumberToString(CurrentNumber);
        public string LastCalledString    => NumberToString(LastCalledNumber);

        public static string NumberToString(int n)
        {
            switch (n)
            {
                case 1:  return "A";
                case 11: return "J";
                case 12: return "Q";
                case 13: return "K";
                default: return n.ToString();
            }
        }

        private readonly Random _rng = new Random();

        public HeartAttackGame()
        {
            Players = new[]
            {
                new Player(0, "玩家",   PlayerType.Human),
                new Player(1, "電腦 A", PlayerType.Computer),
                new Player(2, "電腦 B", PlayerType.Computer),
            };
            Deal();
        }

        private void Deal()
        {
            var deck = new List<Card>();
            foreach (Suit s in Enum.GetValues(typeof(Suit)))
                for (int r = 1; r <= 13; r++)
                    deck.Add(new Card(s, r));

            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                var tmp = deck[i]; deck[i] = deck[j]; deck[j] = tmp;
            }

            for (int i = 0; i < deck.Count; i++)
                Players[i % 3].Deck.Enqueue(deck[i]);
        }

        public (Card card, bool isMatch, int calledNumber) FlipCard()
        {
            LastCalledNumber = CurrentNumber;
            CurrentNumber = CurrentNumber % 13 + 1;

            var player = Players[CurrentPlayerIdx];
            if (player.Deck.Count == 0)
            {
                LastFlippedCard = null;
                return (null, false, LastCalledNumber);
            }

            var card = player.Deck.Dequeue();
            Pile.Add(card);
            LastFlippedCard = card;
            return (card, card.Rank == LastCalledNumber, LastCalledNumber);
        }

        public void AdvanceTurn()
        {
            int tries = 0;
            do
            {
                CurrentPlayerIdx = (CurrentPlayerIdx + 1) % Players.Length;
                tries++;
            }
            while (!Players[CurrentPlayerIdx].IsAlive && tries < Players.Length);
        }

        public void GivePileTo(int playerIdx)
        {
            foreach (var c in Pile)
                Players[playerIdx].Deck.Enqueue(c);
            Pile.Clear();
            LastFlippedCard = null;
            CurrentNumber = 1;   // 牌堆收走後，喊數重置回 A
            LastCalledNumber = 0;
        }

        public int AliveCount => Players.Count(p => p.IsAlive);
        public bool IsGameOver => AliveCount <= 1;
        public Player GetLoser() => Players.FirstOrDefault(p => p.IsAlive);

        /// <summary>指定由哪位玩家開始這一輪</summary>
        public void SetTurn(int playerIdx)
        {
            CurrentPlayerIdx = playerIdx;
        }
    }

    // =========================================================================
    // 防閃爍 Panel（開啟雙緩衝）
    // =========================================================================
    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer, true);
            this.UpdateStyles();
        }
    }

    // =========================================================================
    // frmHeartAttack（主視窗）
    // =========================================================================
    public enum GamePhase
    {
        DifficultySelect,  // 難度選擇畫面
        WaitingForFlip,    // 等待玩家/電腦翻牌
        SlapPhase,         // 數字相同！搶拍中
        ShowingResult,     // 顯示拍牌結果（短暫停頓）
        GameOver           // 遊戲結束
    }

}
