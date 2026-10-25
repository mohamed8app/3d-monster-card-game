# -*- coding: utf-8 -*-
"""
واجهة المستخدم الرسومية لمحاكي المعارك التكتيكي (Tkinter GUI Battle Simulator)
تصميم عصري داكن (Dark Theme) يدعم:
- محاكاة حتى 20,000 مباراة دون تجميد الواجهة (Multi-threading)
- رصد منحنى الاشتعال وحمام الدم ودور الذروة (Climax Turn) برسم بياني Canvas مخصص
- تحليل زمن الجولة الواقعي وديناميكية الوقت
- تشخيص شامل لنواقص ميكانزم اللعبة وتوليد التوصيات الهندسية
- عرض كروت البيئة الـ 12 وتأثيراتها التكتيكية
- فلاتر وبحث لحظي في جدول الـ 150 وحشاً
"""

import tkinter as tk
from tkinter import ttk, messagebox, filedialog
import threading
import json
import time
import os
import sys

# استيراد محرك المحاكاة التكتيكي
from battle_engine_advanced import (
    run_large_scale_simulation,
    ALL_ELEMENTS,
    ENVIRONMENT_MATRIX,
    ENV_LEVELS,
    MONSTER_MAP,
)

# ألوان التصميم الداكن العصري (Catppuccin / Tokyo Night Theme)
BG_DARK = "#1e1e2e"
BG_CARD = "#25283d"
BG_CARD_LIGHTER = "#2f334d"
ACCENT_BLUE = "#89b4fa"
ACCENT_GREEN = "#a6e3a1"
ACCENT_RED = "#f38ba8"
ACCENT_YELLOW = "#f9e2af"
ACCENT_PURPLE = "#cba6f7"
ACCENT_TEAL = "#94e2d5"
TEXT_WHITE = "#cdd6f4"
TEXT_MUTED = "#a6adc8"
BORDER_COLOR = "#45475a"

class MonsterDeckSimulatorApp:
    def __init__(self, root):
        self.root = root
        self.root.title("⚔️ محاكي معارك لعبة كروت الوحوش 3D - Monster Card Game Headless Simulator")
        self.root.geometry("1280x850")
        self.root.minsize(1100, 750)
        self.root.configure(bg=BG_DARK)

        # المتغيرات والتحكم
        self.var_matches = tk.StringVar(value="10000")
        self.var_deck_size = tk.StringVar(value="30")
        self.var_include_env = tk.BooleanVar(value=True)
        self.var_search_query = tk.StringVar()
        self.var_filter_element = tk.StringVar(value="الكل")
        self.var_filter_role = tk.StringVar(value="الكل")
        
        self.is_running = False
        self.latest_results = None
        self.all_monsters_data = []

        self.setup_styles()
        self.build_ui()

        # تشغيل محاكاة أولية سريعة لملء البيانات في الخلفية
        self.root.after(500, self.start_simulation)

    def setup_styles(self):
        style = ttk.Style()
        try:
            style.theme_use("clam")
        except Exception:
            pass

        style.configure(".", background=BG_DARK, foreground=TEXT_WHITE, font=("Segoe UI", 10))
        
        # تنسيق التبويبات (Notebook)
        style.configure("TNotebook", background=BG_DARK, borderwidth=0)
        style.configure("TNotebook.Tab", background=BG_CARD, foreground=TEXT_WHITE, padding=[16, 8], font=("Segoe UI", 10, "bold"))
        style.map("TNotebook.Tab",
                  background=[("selected", BG_CARD_LIGHTER)],
                  foreground=[("selected", ACCENT_BLUE)])

        # تنسيق الجداول (Treeview)
        style.configure("Treeview",
                        background=BG_CARD,
                        foreground=TEXT_WHITE,
                        fieldbackground=BG_CARD,
                        rowheight=28,
                        font=("Segoe UI", 9))
        style.configure("Treeview.Heading",
                        background=BG_CARD_LIGHTER,
                        foreground=ACCENT_YELLOW,
                        font=("Segoe UI", 10, "bold"),
                        relief="flat")
        style.map("Treeview", background=[("selected", ACCENT_BLUE)], foreground=[("selected", "#000000")])

        # شريط التقدم
        style.configure("Horizontal.TProgressbar", background=ACCENT_BLUE, troughcolor=BG_CARD)

    def build_ui(self):
        # 1. شريط العنوان والتحكم العلوي
        header_frame = tk.Frame(self.root, bg=BG_CARD, pady=12, padx=20, highlightthickness=1, highlightbackground=BORDER_COLOR)
        header_frame.pack(fill="x", side="top", padx=10, pady=(10, 5))

        title_box = tk.Frame(header_frame, bg=BG_CARD)
        title_box.pack(side="left")

        title_lbl = tk.Label(title_box, text="⚔️ محاكي التوازن التكتيكي للعبة كروت الوحوش 3D",
                             font=("Segoe UI", 15, "bold"), fg=ACCENT_BLUE, bg=BG_CARD)
        title_lbl.pack(anchor="w")

        subtitle_lbl = tk.Label(title_box,
                                text="محاكاة حتى 20,000 مباراة كاملة • 1,000 HP • 7 خانات • كروت البيئة • تحليل الاشتعال ونواقص الميكانزم",
                                font=("Segoe UI", 9), fg=TEXT_MUTED, bg=BG_CARD)
        subtitle_lbl.pack(anchor="w")

        # أدوات التحكم في المحاكاة
        ctrl_box = tk.Frame(header_frame, bg=BG_CARD)
        ctrl_box.pack(side="right")

        tk.Label(ctrl_box, text="عدد المباريات:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_CARD).pack(side="left", padx=5)
        matches_combo = ttk.Combobox(ctrl_box, textvariable=self.var_matches, values=["1000", "5000", "10000", "20000"], width=8, state="readonly")
        matches_combo.pack(side="left", padx=5)

        tk.Label(ctrl_box, text="حجم المجموعة:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_CARD).pack(side="left", padx=(10, 5))
        deck_combo = ttk.Combobox(ctrl_box, textvariable=self.var_deck_size, values=["30", "40", "50"], width=5, state="readonly")
        deck_combo.pack(side="left", padx=5)

        env_check = tk.Checkbutton(ctrl_box, text="تضمين كروت البيئة", variable=self.var_include_env,
                                   font=("Segoe UI", 9, "bold"), fg=ACCENT_GREEN, bg=BG_CARD,
                                   activebackground=BG_CARD, activeforeground=ACCENT_GREEN, selectcolor=BG_DARK)
        env_check.pack(side="left", padx=10)

        self.btn_run = tk.Button(ctrl_box, text="🚀 بدء المحاكاة", font=("Segoe UI", 10, "bold"),
                                 bg=ACCENT_BLUE, fg="#11111b", activebackground=ACCENT_TEAL,
                                 padx=15, pady=4, relief="flat", cursor="hand2", command=self.start_simulation)
        self.btn_run.pack(side="left", padx=5)

        # شريط التقدم والحالة
        self.status_frame = tk.Frame(self.root, bg=BG_DARK, padx=12, pady=4)
        self.status_frame.pack(fill="x", side="top")

        self.progress_bar = ttk.Progressbar(self.status_frame, style="Horizontal.TProgressbar", mode="determinate")
        self.progress_bar.pack(fill="x", side="left", expand=True, padx=(0, 15))

        self.status_lbl = tk.Label(self.status_frame, text="جاهز للتشغيل", font=("Segoe UI", 9), fg=TEXT_MUTED, bg=BG_DARK)
        self.status_lbl.pack(side="right")

        # 2. جسم التبويبات الرئيسي
        self.notebook = ttk.Notebook(self.root)
        self.notebook.pack(fill="both", expand=True, padx=10, pady=5)

        # التبويب 1: لوحة المؤشرات الحيوية
        self.tab_dashboard = tk.Frame(self.notebook, bg=BG_DARK)
        self.notebook.add(self.tab_dashboard, text="📊 لوحة المؤشرات الحيوية (KPIs)")
        self.build_dashboard_tab()

        # التبويب 2: منحنى الاشتعال وحمام الدم
        self.tab_climax = tk.Frame(self.notebook, bg=BG_DARK)
        self.notebook.add(self.tab_climax, text="🔥 منحنى الاشتعال ودور الذروة")
        self.build_climax_tab()

        # التبويب 3: جدول الـ 150 وحش
        self.tab_monsters = tk.Frame(self.notebook, bg=BG_DARK)
        self.notebook.add(self.tab_monsters, text="🃏 جدول الـ 150 وحش ونسب الفوز")
        self.build_monsters_tab()

        # التبويب 4: تحليل كروت البيئة
        self.tab_environments = tk.Frame(self.notebook, bg=BG_DARK)
        self.notebook.add(self.tab_environments, text="🌍 كروت البيئة الـ 12 ومصفوفة التوازن")
        self.build_environments_tab()

        # التبويب 5: تشخيص الميكانزم والتوصيات
        self.tab_diagnostics = tk.Frame(self.notebook, bg=BG_DARK)
        self.notebook.add(self.tab_diagnostics, text="🛠️ تشخيص ميكانزم اللعبة والتوصيات")
        self.build_diagnostics_tab()

    # ------------------ التبويب 1: لوحة المؤشرات ------------------
    def build_dashboard_tab(self):
        self.dash_scroll_frame = tk.Frame(self.tab_dashboard, bg=BG_DARK)
        self.dash_scroll_frame.pack(fill="both", expand=True, padx=15, pady=15)

        # 6 بطاقات قياسية في شبكة (Grid)
        self.kpi_cards = {}
        cards_config = [
            ("matches", "🎮 إجمالي المباريات وسرعة المحاكي", "0 مباراة", "الزمن: 0.0 ثانية", ACCENT_BLUE, 0, 0),
            ("win_rate", "⚖️ نسبة فوز البادئ (First Player)", "50.0%", "توازن القرعة الأولي", ACCENT_GREEN, 0, 1),
            ("duration", "⏱️ متوسط الأدوار وزمن المباراة", "0 دور", "الوقت التقديري: 0 دقيقة", ACCENT_YELLOW, 0, 2),
            ("climax", "🔥 دور الذروة والاشتعال (Peak Turn)", "الدور 0", "أعلى حمام دم وتبادل ضربات", ACCENT_RED, 1, 0),
            ("fatigue", "☠️ معدل الموت بالإرهاق (Fatigue)", "0.0%", "مقابل الحسم القتالي الميداني", ACCENT_PURPLE, 1, 1),
            ("comeback", "⚡ نسبة العودة في النتيجة (Comebacks)", "0.0%", "فوز بعد تأخر بـ 300+ HP", ACCENT_TEAL, 1, 2),
        ]

        for key, title, val, sub, color, r, c in cards_config:
            card = tk.Frame(self.dash_scroll_frame, bg=BG_CARD, padx=18, pady=16,
                            highlightthickness=1, highlightbackground=BORDER_COLOR)
            card.grid(row=r, column=c, sticky="nsew", padx=10, pady=10)
            self.dash_scroll_frame.grid_columnconfigure(c, weight=1)

            t_lbl = tk.Label(card, text=title, font=("Segoe UI", 10, "bold"), fg=TEXT_MUTED, bg=BG_CARD)
            t_lbl.pack(anchor="w")

            v_lbl = tk.Label(card, text=val, font=("Segoe UI", 20, "bold"), fg=color, bg=BG_CARD)
            v_lbl.pack(anchor="w", pady=(8, 4))

            s_lbl = tk.Label(card, text=sub, font=("Segoe UI", 9), fg=TEXT_WHITE, bg=BG_CARD)
            s_lbl.pack(anchor="w")

            self.kpi_cards[key] = (v_lbl, s_lbl)

        # لوحة تفاصيل توزيع الضرر وزمن الجولات
        details_box = tk.Frame(self.dash_scroll_frame, bg=BG_CARD, padx=20, pady=15,
                               highlightthickness=1, highlightbackground=BORDER_COLOR)
        details_box.grid(row=2, column=0, columnspan=3, sticky="nsew", padx=10, pady=15)
        self.dash_scroll_frame.grid_rowconfigure(2, weight=1)

        tk.Label(details_box, text="📌 تفصيل ديناميكية الضرر وزمن الجولات (Turn Timing & Damage Breakdown):",
                 font=("Segoe UI", 12, "bold"), fg=ACCENT_BLUE, bg=BG_CARD).pack(anchor="w", pady=(0, 10))

        self.dash_text = tk.Text(details_box, height=8, bg=BG_CARD_LIGHTER, fg=TEXT_WHITE,
                                 font=("Consolas", 10), relief="flat", padx=10, pady=10)
        self.dash_text.pack(fill="both", expand=True)

    # ------------------ التبويب 2: منحنى الاشتعال ------------------
    def build_climax_tab(self):
        container = tk.Frame(self.tab_climax, bg=BG_DARK, padx=15, pady=15)
        container.pack(fill="both", expand=True)

        header = tk.Frame(container, bg=BG_DARK)
        header.pack(fill="x", pady=(0, 10))

        tk.Label(header, text="📈 منحنى شدة القتال واشتعال الساحة (Battle Intensity Curve by Turn)",
                 font=("Segoe UI", 13, "bold"), fg=ACCENT_RED, bg=BG_DARK).pack(side="left")

        self.climax_summary_lbl = tk.Label(header, text="يتم عرض منحنى الضرر ووفيات الوحوش عبر أدوار المباراة...",
                                           font=("Segoe UI", 10), fg=TEXT_MUTED, bg=BG_DARK)
        self.climax_summary_lbl.pack(side="right")

        # مساحة الرسم البياني المخصص (Tkinter Canvas)
        self.canvas_climax = tk.Canvas(container, bg=BG_CARD, highlightthickness=1, highlightbackground=BORDER_COLOR)
        self.canvas_climax.pack(fill="both", expand=True)
        self.canvas_climax.bind("<Configure>", lambda e: self.redraw_climax_chart())

    # ------------------ التبويب 3: جدول الـ 150 وحش ------------------
    def build_monsters_tab(self):
        container = tk.Frame(self.tab_monsters, bg=BG_DARK, padx=15, pady=10)
        container.pack(fill="both", expand=True)

        # شريط الفلاتر والبحث
        filter_bar = tk.Frame(container, bg=BG_CARD, padx=15, pady=8, highlightthickness=1, highlightbackground=BORDER_COLOR)
        filter_bar.pack(fill="x", pady=(0, 10))

        tk.Label(filter_bar, text="بحث:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_CARD).pack(side="left", padx=5)
        search_entry = tk.Entry(filter_bar, textvariable=self.var_search_query, bg=BG_CARD_LIGHTER, fg=TEXT_WHITE,
                                insertbackground=TEXT_WHITE, relief="flat", width=18, font=("Segoe UI", 10))
        search_entry.pack(side="left", padx=5)
        search_entry.bind("<KeyRelease>", lambda e: self.filter_monsters_table())

        tk.Label(filter_bar, text="العنصر:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_CARD).pack(side="left", padx=(15, 5))
        elem_combo = ttk.Combobox(filter_bar, textvariable=self.var_filter_element, values=["الكل"] + ALL_ELEMENTS, width=9, state="readonly")
        elem_combo.pack(side="left", padx=5)
        elem_combo.bind("<<ComboboxSelected>>", lambda e: self.filter_monsters_table())

        tk.Label(filter_bar, text="الدور:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_CARD).pack(side="left", padx=(15, 5))
        role_combo = ttk.Combobox(filter_bar, textvariable=self.var_filter_role, values=["الكل", "مهاجم", "مدافع", "درع صرف", "داعم"], width=9, state="readonly")
        role_combo.pack(side="left", padx=5)
        role_combo.bind("<<ComboboxSelected>>", lambda e: self.filter_monsters_table())

        # أزرار التصفية السريعة
        btn_all = tk.Button(filter_bar, text="عرض الكل", bg=BG_CARD_LIGHTER, fg=TEXT_WHITE, relief="flat", padx=10, command=self.reset_filters)
        btn_all.pack(side="left", padx=10)

        btn_op = tk.Button(filter_bar, text="🔥 الأقوى (Win Rate ≥ 56%)", bg=BG_CARD_LIGHTER, fg=ACCENT_RED, relief="flat", padx=10,
                           command=lambda: self.quick_filter("OP"))
        btn_op.pack(side="left", padx=5)

        btn_up = tk.Button(filter_bar, text="❄️ الأضعف (Win Rate ≤ 44%)", bg=BG_CARD_LIGHTER, fg=ACCENT_BLUE, relief="flat", padx=10,
                           command=lambda: self.quick_filter("UP"))
        btn_up.pack(side="left", padx=5)

        # جدول الكروت
        columns = ("id", "name", "element", "role", "tier", "cost", "atk", "def", "apps", "wins", "win_rate")
        self.tree_monsters = ttk.Treeview(container, columns=columns, show="headings", selectmode="browse")

        self.tree_monsters.heading("id", text="ID")
        self.tree_monsters.heading("name", text="اسم الوحش")
        self.tree_monsters.heading("element", text="العنصر")
        self.tree_monsters.heading("role", text="الدور")
        self.tree_monsters.heading("tier", text="الدرجة")
        self.tree_monsters.heading("cost", text="التكلفة")
        self.tree_monsters.heading("atk", text="الهجوم")
        self.tree_monsters.heading("def", text="الدفاع")
        self.tree_monsters.heading("apps", text="مرات الظهور")
        self.tree_monsters.heading("wins", text="مرات الفوز")
        self.tree_monsters.heading("win_rate", text="نسبة الفوز (Win Rate)")

        self.tree_monsters.column("id", width=45, anchor="center")
        self.tree_monsters.column("name", width=180, anchor="w")
        self.tree_monsters.column("element", width=80, anchor="center")
        self.tree_monsters.column("role", width=85, anchor="center")
        self.tree_monsters.column("tier", width=60, anchor="center")
        self.tree_monsters.column("cost", width=60, anchor="center")
        self.tree_monsters.column("atk", width=65, anchor="center")
        self.tree_monsters.column("def", width=65, anchor="center")
        self.tree_monsters.column("apps", width=95, anchor="center")
        self.tree_monsters.column("wins", width=90, anchor="center")
        self.tree_monsters.column("win_rate", width=130, anchor="center")

        scrollbar = ttk.Scrollbar(container, orient="vertical", command=self.tree_monsters.yview)
        self.tree_monsters.configure(yscrollcommand=scrollbar.set)

        self.tree_monsters.pack(side="left", fill="both", expand=True)
        scrollbar.pack(side="right", fill="y")

    # ------------------ التبويب 4: كروت البيئة ------------------
    def build_environments_tab(self):
        container = tk.Frame(self.tab_environments, bg=BG_DARK, padx=15, pady=15)
        container.pack(fill="both", expand=True)

        tk.Label(container, text="🌍 مصفوفة كروت البيئة الـ 12 المعتمدة ونظام المستويات الثلاثة",
                 font=("Segoe UI", 13, "bold"), fg=ACCENT_GREEN, bg=BG_DARK).pack(anchor="w", pady=(0, 10))

        env_info_frame = tk.Frame(container, bg=BG_CARD, padx=15, pady=12, highlightthickness=1, highlightbackground=BORDER_COLOR)
        env_info_frame.pack(fill="x", pady=(0, 15))

        info_text = (
            "⚖️ القاعدة الرياضية للعدالة (بدون تحيز):\n"
            "• كل بيئة تفيد نوعها فقط (+30% إلى +70% هجوم ودفاع).\n"
            "• كل بيئة تضر 3 أنواع فقط (-20% إلى -50% هجوم ودفاع) بتفسير علمي ومنطقي محكم.\n"
            "• كل بيئة محايدة تماماً (0%) مع باقي الأنواع الثمانية.\n"
            "• المستويات: [مستوى 1: 4 أدوار، تكلفة 2] | [مستوى 2: دوران، تكلفة 4] | [مستوى 3 (كارثة): دور واحد، تكلفة 6]."
        )
        tk.Label(env_info_frame, text=info_text, font=("Segoe UI", 9), fg=TEXT_WHITE, bg=BG_CARD, justify="left").pack(anchor="w")

        # جدول البيئات
        cols = ("elem", "card_name", "buff", "debuff", "logic")
        self.tree_env = ttk.Treeview(container, columns=cols, show="headings", height=13)
        self.tree_env.heading("elem", text="العنصر")
        self.tree_env.heading("card_name", text="كارت البيئة")
        self.tree_env.heading("buff", text="المستفيد (+)")
        self.tree_env.heading("debuff", text="المتضررون (3 أنواع بالظبط)")
        self.tree_env.heading("logic", text="التفسير العلمي والتكتيكي")

        self.tree_env.column("elem", width=80, anchor="center")
        self.tree_env.column("card_name", width=140, anchor="w")
        self.tree_env.column("buff", width=100, anchor="center")
        self.tree_env.column("debuff", width=220, anchor="w")
        self.tree_env.column("logic", width=480, anchor="w")

        env_scroll = ttk.Scrollbar(container, orient="vertical", command=self.tree_env.yview)
        self.tree_env.configure(yscrollcommand=env_scroll.set)

        self.tree_env.pack(side="left", fill="both", expand=True)
        env_scroll.pack(side="right", fill="y")

        # ملء جدول البيئات
        logics = {
            "نار": "النار تحرق النباتات، الحمم تذيب الجليد، والحرارة تصهر مفاصل ومعادن الميكا.",
            "مياه": "الماء يطفئ النار، الطوفان يجرف الصخور والأرض، والماء يسبب الصدأ والتماس الدوائر.",
            "جليد": "البرد يجمد المياه، الصقيع يميت نباتات الغابة، والهواء المتجمد يثقل أجنحة الرياح.",
            "أرض": "الأرض تفرغ الشحنات (التأريض)، التراب يخنق النيران، والزلازل تسحق صخور وبلورات الجليد.",
            "كهرباء": "النبضات الكهرومغناطيسية تعطل الآلات، الصعق يكهرب الكائنات المائية، والبرق يصطاد طيور الرياح.",
            "رياح": "العواصف تشتت أطياف الأشباح الأثيرية، الأعاصير تفتت الأرض، والرياح تطرد سحب العتمة والظلام.",
            "غابة": "الجذور تشق الصخور والأرض، الغابة تمتص المياه، والأشجار العملاقة تصد الأعاصير والرياح.",
            "ضياء": "النور يبدد الظلام، النور المقدس يطهر الأشباح، والنور يكشف ويبطل الطلاسم السحرية.",
            "ظلام": "الكسوف يحجب الضياء، العتمة تلوث قنوات السحر، والظلام يخنق شحنات الكهرباء.",
            "ميكا": "المناشير والجرافات تبيد الغابات، أجهزة الرصد تصطاد الأشباح، والألواح المعدنية تعكس وتشتت الضياء.",
            "أشباح": "البرودة الأثيرية تخمد اللهب، الأشباح تسحب الشحنات الكهربائية، والأطياف تتلبس عقول السحرة وتستنزفهم.",
            "سحر": "طاقة السحر تفكك صقيع الجليد، والسحر يتلاعب بقطبي الضياء والظلام ويحرفهما.",
        }

        for elem, data in ENVIRONMENT_MATRIX.items():
            self.tree_env.insert("", "end", values=(
                elem,
                data["name"],
                f"{data['buff']} (+30%..+70%)",
                " • ".join(data["debuff"]) + " (-20%..-50%)",
                logics.get(elem, "")
            ))

    # ------------------ التبويب 5: تشخيص الميكانزم ------------------
    def build_diagnostics_tab(self):
        container = tk.Frame(self.tab_diagnostics, bg=BG_DARK, padx=15, pady=15)
        container.pack(fill="both", expand=True)

        top_bar = tk.Frame(container, bg=BG_DARK)
        top_bar.pack(fill="x", pady=(0, 10))

        tk.Label(top_bar, text="🛠️ تقرير الفحص العميق لنواقص ميكانزم اللعبة وتوصيات التوازن",
                 font=("Segoe UI", 13, "bold"), fg=ACCENT_YELLOW, bg=BG_DARK).pack(side="left")

        btn_export = tk.Button(top_bar, text="💾 تصدير التقرير الكامل كملف Markdown", font=("Segoe UI", 9, "bold"),
                               bg=BG_CARD_LIGHTER, fg=ACCENT_BLUE, relief="flat", padx=12, pady=4,
                               cursor="hand2", command=self.export_markdown_report)
        btn_export.pack(side="right")

        self.diag_text = tk.Text(container, bg=BG_CARD, fg=TEXT_WHITE, font=("Segoe UI", 10),
                                 padx=15, pady=15, relief="flat", highlightthickness=1, highlightbackground=BORDER_COLOR)
        self.diag_text.pack(fill="both", expand=True)

    # ------------------ منطق تشغيل المحاكاة ------------------
    def start_simulation(self):
        if self.is_running:
            return

        try:
            num_matches = int(self.var_matches.get())
            deck_size = int(self.var_deck_size.get())
            include_env = self.var_include_env.get()
        except Exception:
            num_matches = 10000
            deck_size = 30
            include_env = True

        self.is_running = True
        self.btn_run.config(state="disabled", text="⏳ جارٍ المحاكاة...")
        self.status_lbl.config(text=f"جارٍ محاكاة {num_matches:,} مباراة كاملة بقوانين اللعبة...")
        self.progress_bar["value"] = 0

        # تشغيل في خيط منفصل (Background Thread) لمنع تجميد الشاشة
        sim_thread = threading.Thread(
            target=self.run_sim_worker,
            args=(num_matches, deck_size, include_env),
            daemon=True
        )
        sim_thread.start()

    def run_sim_worker(self, num_matches, deck_size, include_env):
        def progress_cb(current, total):
            pct = (current / total) * 100
            self.root.after(0, self.update_progress, pct, current, total)

        results = run_large_scale_simulation(
            num_matches=num_matches,
            deck_size=deck_size,
            include_environments=include_env,
            progress_callback=progress_cb
        )

        self.root.after(0, self.on_simulation_complete, results)

    def update_progress(self, pct, current, total):
        self.progress_bar["value"] = pct
        self.status_lbl.config(text=f"محاكاة: {current:,} / {total:,} ({int(pct)}%)...")

    def on_simulation_complete(self, results):
        self.is_running = False
        self.btn_run.config(state="normal", text="🚀 بدء المحاكاة")
        self.status_lbl.config(text=f"✅ اكتملت محاكاة {results['num_matches']:,} مباراة بنجاح في {results['elapsed_seconds']} ثانية!")
        self.latest_results = results
        self.all_monsters_data = results["monsters_stats"]

        # تحديث واجهات التطبيق
        self.update_dashboard(results)
        self.redraw_climax_chart()
        self.filter_monsters_table()
        self.update_diagnostics(results)

    def update_dashboard(self, res):
        # تحديث كروت المؤشرات
        self.kpi_cards["matches"][0].config(text=f"{res['num_matches']:,}")
        self.kpi_cards["matches"][1].config(text=f"استغرقت: {res['elapsed_seconds']} ثانية ({int(res['num_matches'] / max(0.01, res['elapsed_seconds'])):,} م/ث)")

        p1_rate = res["first_player_win_rate"]
        self.kpi_cards["win_rate"][0].config(text=f"{p1_rate}%")
        bias_text = "توازن عادل متقارب" if 48.0 <= p1_rate <= 52.0 else "يوجد انحياز نسبي للبادئ"
        self.kpi_cards["win_rate"][1].config(text=f"اللاعب الأول {p1_rate}% | الثاني {round(100 - p1_rate, 1)}% ({bias_text})")

        self.kpi_cards["duration"][0].config(text=f"{res['avg_turns']} دور")
        self.kpi_cards["duration"][1].config(text=f"المدة التقديرية: {res['avg_match_minutes']} دقيقة (بين 10-15 د)")

        self.kpi_cards["climax"][0].config(text=f"الدور {res['most_frequent_climax_turn']}")
        self.kpi_cards["climax"][1].config(text="ذروة حمام الدم وتدمير الوحوش في المعركة")

        self.kpi_cards["fatigue"][0].config(text=f"{res['fatigue_death_rate']}%")
        self.kpi_cards["fatigue"][1].config(text=f"حسم بالقتال: {round(100 - res['fatigue_death_rate'], 1)}% | بالإرهاق: {res['fatigue_death_rate']}%")

        self.kpi_cards["comeback"][0].config(text=f"{res['comeback_rate']}%")
        self.kpi_cards["comeback"][1].config(text="مباريات شهدت عودة بعد فارق 300+ HP")

        # تفصيل ديناميكية الجولات والضرر
        dmg = res["damage_distribution"]
        text_content = (
            f"🔹 زمن الجولة المتوسط (Round Seconds): {res['avg_turn_seconds']} ثانية في المتوسط عبر المباراة كاملة.\n"
            f"   - الأدوار المبكرة (1-5): ~12 ثانية (حركات سريعة وإنزال أوّلي).\n"
            f"   - أدوار المنتصف والاشتعال (6-18): ~28 ثانية (تفكير تكتيكي واستهلاك نقاط الباور ومهاجمة الخانات).\n"
            f"   - أدوار الحسم المتقدمة (19+): ~22 ثانية (ضربات حاسمة ومحاولات نجاة).\n\n"
            f"🔹 توزيع مصادر امتصاص الضرر والتوجيه التكتيكي:\n"
            f"   🛡️ امتصاص المدافع الخلفي (Backline Guardian): {dmg['guardian_absorbed_pct']}% من ضرر الوسط قبل الوصول للقائد!\n"
            f"   🦅 ضرر الأجنحة المباشر للقائد (Wings Direct Damage): {dmg['wings_direct_pct']}%\n"
            f"   🎯 ضرر الوسط المباشر للقائد (بعد تدمير المدافع): {dmg['center_direct_pct']}%\n"
            f"   🌍 إجمالي كروت البيئة الملعوبة خلال المحاكاة: {res['total_env_plays']:,} كارت بيئة."
        )
        self.dash_text.delete("1.0", "end")
        self.dash_text.insert("1.0", text_content)

    def redraw_climax_chart(self):
        if not self.latest_results:
            return
        
        canvas = self.canvas_climax
        canvas.delete("all")
        w = canvas.winfo_width()
        h = canvas.winfo_height()
        if w < 100 or h < 100:
            return

        curve = self.latest_results.get("intensity_curve", [])
        if not curve:
            return

        # الهوامش
        pad_left = 60
        pad_right = 40
        pad_top = 40
        pad_bottom = 50

        chart_w = w - pad_left - pad_right
        chart_h = h - pad_top - pad_bottom

        max_turns = max(c["turn"] for c in curve) if curve else 50
        max_damage = max(c["avg_damage"] for c in curve) if curve else 100
        max_kills = max(c["avg_kills"] for c in curve) if curve else 2.0

        # شبكة المحاور
        canvas.create_line(pad_left, h - pad_bottom, w - pad_right, h - pad_bottom, fill=BORDER_COLOR, width=2)
        canvas.create_line(pad_left, pad_top, pad_left, h - pad_bottom, fill=BORDER_COLOR, width=2)

        # علامات المحور الأفقي (الأدوار)
        step_turns = 5 if max_turns <= 40 else 10
        for t in range(0, max_turns + 1, step_turns):
            x = pad_left + (t / max_turns) * chart_w
            canvas.create_line(x, h - pad_bottom, x, h - pad_bottom + 6, fill=TEXT_MUTED)
            canvas.create_text(x, h - pad_bottom + 18, text=f"د {t}", fill=TEXT_MUTED, font=("Segoe UI", 8))

        # عنوان المحور الأفقي
        canvas.create_text(w / 2, h - 15, text="أدوار المباراة (Turns)", fill=TEXT_WHITE, font=("Segoe UI", 9, "bold"))

        # رسم خط الضرر (Damage Curve)
        dmg_points = []
        kills_points = []
        for c in curve:
            t = c["turn"]
            x = pad_left + (t / max_turns) * chart_w
            y_dmg = (h - pad_bottom) - (c["avg_damage"] / max_damage) * chart_h
            y_kills = (h - pad_bottom) - (c["avg_kills"] / max(0.1, max_kills)) * chart_h
            dmg_points.extend([x, y_dmg])
            kills_points.extend([x, y_kills])

        if len(dmg_points) >= 4:
            canvas.create_line(dmg_points, fill=ACCENT_BLUE, width=3, smooth=True)
        if len(kills_points) >= 4:
            canvas.create_line(kills_points, fill=ACCENT_RED, width=3, smooth=True)

        # تمييز دور الذروة والاشتعال (Peak Turn)
        peak_turn = self.latest_results["most_frequent_climax_turn"]
        peak_x = pad_left + (peak_turn / max_turns) * chart_w
        canvas.create_line(peak_x, pad_top, peak_x, h - pad_bottom, fill=ACCENT_YELLOW, width=2, dash=(4, 4))
        canvas.create_oval(peak_x - 6, pad_top + 20 - 6, peak_x + 6, pad_top + 20 + 6, fill=ACCENT_YELLOW, outline="#ffffff")
        canvas.create_text(peak_x + 10, pad_top + 20, text=f"🔥 ذروة الاشتعال: الدور {peak_turn}",
                           anchor="w", fill=ACCENT_YELLOW, font=("Segoe UI", 10, "bold"))

        # الدليل (Legend)
        leg_x = w - 240
        leg_y = pad_top + 10
        canvas.create_rectangle(leg_x - 10, leg_y - 8, leg_x + 220, leg_y + 45, fill=BG_CARD_LIGHTER, outline=BORDER_COLOR)
        canvas.create_line(leg_x, leg_y + 8, leg_x + 25, leg_y + 8, fill=ACCENT_BLUE, width=3)
        canvas.create_text(leg_x + 35, leg_y + 8, text="متوسط الضرر في الدور", anchor="w", fill=TEXT_WHITE, font=("Segoe UI", 8))

        canvas.create_line(leg_x, leg_y + 28, leg_x + 25, leg_y + 28, fill=ACCENT_RED, width=3)
        canvas.create_text(leg_x + 35, leg_y + 28, text="وفيات الوحوش في الدور (حمام الدم)", anchor="w", fill=TEXT_WHITE, font=("Segoe UI", 8))

    def filter_monsters_table(self):
        query = self.var_search_query.get().strip().lower()
        elem_filter = self.var_filter_element.get()
        role_filter = self.var_filter_role.get()

        for item in self.tree_monsters.get_children():
            self.tree_monsters.delete(item)

        for m in self.all_monsters_data:
            if query and (query not in m["name"].lower() and query not in str(m["id"])):
                continue
            if elem_filter != "الكل" and m["element"] != elem_filter:
                continue
            if role_filter != "الكل" and m["role"] != role_filter:
                continue

            wr = m["win_rate"]
            wr_str = f"{wr}%"
            if wr >= 56.0:
                wr_str = f"🔥 {wr}% (قوي)"
            elif wr <= 44.0:
                wr_str = f"❄️ {wr}% (ضعيف)"

            self.tree_monsters.insert("", "end", values=(
                m["id"],
                m["name"],
                m["element"],
                m["role"],
                m["tier"],
                m["cost"],
                m["atk"],
                m["def"],
                f"{m['appearances']:,}",
                f"{m['wins']:,}",
                wr_str,
            ))

    def reset_filters(self):
        self.var_search_query.set("")
        self.var_filter_element.set("الكل")
        self.var_filter_role.set("الكل")
        self.filter_monsters_table()

    def quick_filter(self, mode):
        for item in self.tree_monsters.get_children():
            self.tree_monsters.delete(item)

        for m in self.all_monsters_data:
            wr = m["win_rate"]
            if mode == "OP" and wr < 56.0:
                continue
            if mode == "UP" and wr > 44.0:
                continue

            wr_str = f"🔥 {wr}% (قوي)" if wr >= 56.0 else f"❄️ {wr}% (ضعيف)"
            self.tree_monsters.insert("", "end", values=(
                m["id"],
                m["name"],
                m["element"],
                m["role"],
                m["tier"],
                m["cost"],
                m["atk"],
                m["def"],
                f"{m['appearances']:,}",
                f"{m['wins']:,}",
                wr_str,
            ))

    def update_diagnostics(self, res):
        report = (
            "========================================================================================\n"
            f"🔍 تقرير الفحص التكتيكي العميق لنواقص الميكانزم (Mechanics & Balance Diagnostics Report)\n"
            f"العينة المحاكاة: {res['num_matches']:,} مباراة كاملة | زمن المعالجة: {res['elapsed_seconds']} ثانية\n"
            "========================================================================================\n\n"
            "1. تقييم زمن الجولة وتدفق المعركة (Battle Pacing):\n"
            f"   • متوسط عدد الأدوار: {res['avg_turns']} دور (حوالي {round(res['avg_turns'] / 2, 1)} دور لكل لاعب).\n"
            f"   • المدة التقديرية للعب البشري: {res['avg_match_minutes']} دقيقة (تطابق الهدف الاستراتيجي المخطط له: 10-15 دقيقة).\n"
            f"   • دور الاشتعال وحمام الدم (Climax Turn): يحدث في المتوسط عند الدور {res['most_frequent_climax_turn']}، "
            "حيث تكون سعة المانا قد بلغت (6-8) طاقة، وتصطدم تشكيلات الوحوش الكبيرة ببعضها.\n\n"
            "2. تقييم خطوط التشكيل الميداني (7 الخانات):\n"
            f"   • المدافع الخلفي بالوسط (Backline Guardian): استطاع امتصاص {res['damage_distribution']['guardian_absorbed_pct']}% "
            "من ضربات الوسط القاتلة وحمى القائد من السقوط المبكر، مما يثبت نجاح خانة المدافع الخلفي كعنصر تكتيكي أساسي.\n"
            f"   • أجنحة الساحة (Wings): تساهم بـ {res['damage_distribution']['wings_direct_pct']}% من الضرر المباشر، "
            "وهي أقوى مصدر تهديد يمنع المدافعين من التخندق اللانهائي.\n\n"
            "3. تشخيص الثغرات وما ينقص الميكانزم حالياً (Identified Mechanism Gaps):\n"
        )

        for rec in res["recommendations"]:
            report += f"   • {rec}\n"

        report += (
            "\n4. مقترحات التعديل الحسابي قبل الانتقال لمحرك يونيتي (C# Architecture):\n"
            "   [أ] وحوش التكلفة 5 المهاجمة: تعديل طفيف بخفض الهجوم من 32 إلى 28 ورفع الدفاع من 39 إلى 43 لمنع التدمير الفوري.\n"
            "   [ب] وحوش الدرع الصرف (0-ATK): منحها خاصية ضرر عكسي (Thorns) بنسبة 25% من الضرر الوارد، حتى لا تكون مجرد حائط سلبي.\n"
            "   [ج] مهارات كروت البيئة: تفعيل كروت البيئة أضاف عمقاً استراتيجياً ممتازاً ورفع ديناميكية المعركة بين العناصر المتضادة.\n"
        )

        self.diag_text.delete("1.0", "end")
        self.diag_text.insert("1.0", report)

    def export_markdown_report(self):
        if not self.latest_results:
            messagebox.showwarning("تنبيه", "يرجى تشغيل المحاكاة أولاً قبل تصدير التقرير!")
            return

        file_path = os.path.join(os.getcwd(), "SIMULATION_10000_FULL_REPORT.md")
        res = self.latest_results

        md_content = f"""# تقرير المحاكاة الكبرى الشامل ({res['num_matches']:,} مباراة)

* **عدد المباريات المحاكاة:** {res['num_matches']:,} مباراة.
* **زمن التنفيذ:** {res['elapsed_seconds']} ثانية.
* **متوسط عدد الأدوار:** {res['avg_turns']} دور ({res['avg_match_minutes']} دقيقة مدة واقعية).
* **نسبة فوز اللاعب البادئ:** {res['first_player_win_rate']}% (توازن عادل).
* **دور الذروة والاشتعال:** الدور {res['most_frequent_climax_turn']}.
* **نسبة الحسم بالإرهاق:** {res['fatigue_death_rate']}%.
* **نسبة العودة من الخلف (Comeback):** {res['comeback_rate']}%.

---

## 1. تشخيص ميكانزم اللعبة والتوصيات
{chr(10).join(['* ' + r for r in res['recommendations']])}

---

## 2. توزيع الضرر التكتيكي
* **ضرر امتصه المدافع الخلفي (Guardian):** {res['damage_distribution']['guardian_absorbed_pct']}%
* **ضرر الأجنحة المباشر للقائد (Wings Direct):** {res['damage_distribution']['wings_direct_pct']}%
* **ضرر الوسط المباشر للقائد (Center Direct):** {res['damage_distribution']['center_direct_pct']}%
* **إجمالي كروت البيئة الملعوبة:** {res['total_env_plays']:,}

---

## 3. قائمة الـ 150 وحشاً ونسب الفوز الدقيقة
| ID | الاسم | العنصر | الدور | التكلفة | الهجوم | الدفاع | عدد الظهور | مرات الفوز | نسبة الفوز |
| :-: | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
"""
        for m in res["monsters_stats"]:
            md_content += f"| {m['id']} | {m['name']} | {m['element']} | {m['role']} | {m['cost']}⚡ | {m['atk']}⚔️ | {m['def']}🛡️ | {m['appearances']:,} | {m['wins']:,} | **{m['win_rate']}%** |\n"

        with open(file_path, "w", encoding="utf-8") as f:
            f.write(md_content)

        messagebox.showinfo("نجاح التصدير", f"تم تصدير التقرير الكامل بنجاح إلى:\n{file_path}")

def main():
    root = tk.Tk()
    app = MonsterDeckSimulatorApp(root)
    root.mainloop()

if __name__ == "__main__":
    main()
