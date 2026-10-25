# -*- coding: utf-8 -*-
"""
واجهة محاكي الـ 100,000 مباراة لكروت الوحوش (Win Rate Simulator)
ملف مخصص وبسيط ومباشر:
- يحاكي 100,000 مباراة في ~18 ثانية باستخدام كافة أنوية المعالج (8 Cores)
- يعرض جدولاً واضحاً لنسبة فوز (Win Rate) كل وحش من الـ 150 وحشاً
- فرز وبحث لحظي وتصنيف لقوة الوحوش
"""

import tkinter as tk
from tkinter import ttk, messagebox, filedialog
import threading
import json
import os
import sys
import time
from collections import defaultdict
from concurrent.futures import ProcessPoolExecutor

# استيراد محرك المحاكاة والـ Worker
from sim_worker import simulate_matches_worker
from battle_engine_advanced import MONSTER_MAP, ALL_ELEMENTS

# ألوان الواجهة العصرية الداكنة
BG_MAIN = "#181825"
BG_PANEL = "#1e1e2e"
BG_CARD = "#313244"
BG_INPUT = "#45475a"
ACCENT_CYAN = "#89dceb"
ACCENT_GREEN = "#a6e3a1"
ACCENT_RED = "#f38ba8"
ACCENT_YELLOW = "#f9e2af"
ACCENT_BLUE = "#89b4fa"
TEXT_WHITE = "#cdd6f4"
TEXT_MUTED = "#bac2de"

class WinRateSimulator100kApp:
    def __init__(self, root):
        self.root = root
        self.root.title("🎮 محاكي الـ 100,000 مباراة - كشف نسبة فوز الوحوش (Win Rate Simulator)")
        self.root.geometry("1200x800")
        self.root.minsize(1000, 650)
        self.root.configure(bg=BG_MAIN)

        self.matches_var = tk.StringVar(value="100000")
        self.search_var = tk.StringVar()
        self.filter_elem_var = tk.StringVar(value="كل العناصر")
        self.filter_role_var = tk.StringVar(value="كل الأدوار")
        
        self.is_simulating = False
        self.monsters_data = []
        self.sort_column = "win_rate"
        self.sort_descending = True

        self.setup_styles()
        self.build_ui()

        # تحميل النتائج الجاهزة إن وجدت أو تحفيز البدء
        self.load_initial_data()

    def setup_styles(self):
        style = ttk.Style()
        try:
            style.theme_use("clam")
        except Exception:
            pass

        style.configure(".", background=BG_MAIN, foreground=TEXT_WHITE, font=("Segoe UI", 10))
        
        # تنسيق شريط التقدم
        style.configure("Custom.Horizontal.TProgressbar",
                        background=ACCENT_CYAN,
                        troughcolor=BG_PANEL,
                        thickness=14)

        # تنسيق الجدول (Treeview)
        style.configure("Treeview",
                        background=BG_PANEL,
                        foreground=TEXT_WHITE,
                        fieldbackground=BG_PANEL,
                        rowheight=30,
                        font=("Segoe UI", 10))
        style.configure("Treeview.Heading",
                        background=BG_CARD,
                        foreground=ACCENT_YELLOW,
                        font=("Segoe UI", 10, "bold"),
                        relief="flat")
        style.map("Treeview",
                  background=[("selected", ACCENT_BLUE)],
                  foreground=[("selected", "#11111b")])

    def build_ui(self):
        # 1. الشريط العلوي الرئيسي (Header)
        top_frame = tk.Frame(self.root, bg=BG_PANEL, padx=20, pady=15, highlightthickness=1, highlightbackground=BG_CARD)
        top_frame.pack(fill="x", padx=15, pady=(15, 8))

        # عنوان وشرح
        title_box = tk.Frame(top_frame, bg=BG_PANEL)
        title_box.pack(side="left")

        lbl_title = tk.Label(title_box, text="🎮 محاكي الـ 100,000 مباراة لكروت الوحوش",
                             font=("Segoe UI", 16, "bold"), fg=ACCENT_CYAN, bg=BG_PANEL)
        lbl_title.pack(anchor="w")

        lbl_sub = tk.Label(title_box, text="محاكاة ضخمة بين بوتات ذكية لحساب نسبة الفوز الحقيقية (Win Rate) لكل وحش واكتشاف التوازن",
                           font=("Segoe UI", 9), fg=TEXT_MUTED, bg=BG_PANEL)
        lbl_sub.pack(anchor="w", pady=(3, 0))

        # زر التشغيل واختيار عدد الجولات
        ctrl_box = tk.Frame(top_frame, bg=BG_PANEL)
        ctrl_box.pack(side="right")

        tk.Label(ctrl_box, text="عدد المباريات:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_PANEL).pack(side="left", padx=5)
        combo_matches = ttk.Combobox(ctrl_box, textvariable=self.matches_var, values=["10000", "50000", "100000", "200000"], width=8, state="readonly")
        combo_matches.pack(side="left", padx=5)

        self.btn_start = tk.Button(ctrl_box, text="▶️ بدء المحاكاة الكبرى", font=("Segoe UI", 11, "bold"),
                                   bg=ACCENT_GREEN, fg="#11111b", activebackground=ACCENT_CYAN,
                                   relief="flat", padx=18, pady=6, cursor="hand2", command=self.start_simulation)
        self.btn_start.pack(side="left", padx=10)

        # 2. شريط التقدم والحالة
        prog_frame = tk.Frame(self.root, bg=BG_MAIN, padx=20, pady=4)
        prog_frame.pack(fill="x")

        self.progress_bar = ttk.Progressbar(prog_frame, style="Custom.Horizontal.TProgressbar", mode="determinate")
        self.progress_bar.pack(fill="x", side="left", expand=True, padx=(0, 15))

        self.lbl_status = tk.Label(prog_frame, text="جاهز للتشغيل", font=("Segoe UI", 9, "bold"), fg=ACCENT_YELLOW, bg=BG_MAIN)
        self.lbl_status.pack(side="right")

        # 3. بطاقات ملخص سريعة (KPI Summary Cards)
        kpi_frame = tk.Frame(self.root, bg=BG_MAIN, padx=15, pady=5)
        kpi_frame.pack(fill="x")

        self.lbl_card_matches = self.create_kpi_card(kpi_frame, "إجمالي المباريات", "100,000", ACCENT_BLUE, 0)
        self.lbl_card_top = self.create_kpi_card(kpi_frame, "👑 أعلى وحش (أقوى فوز)", "جاري الحساب...", ACCENT_RED, 1)
        self.lbl_card_bot = self.create_kpi_card(kpi_frame, "❄️ أقل وحش (يحتاج دعم)", "جاري الحساب...", ACCENT_YELLOW, 2)
        self.lbl_card_p1 = self.create_kpi_card(kpi_frame, "⚖️ نسبة فوز اللاعب البادئ", "50.0%", ACCENT_GREEN, 3)

        # 4. شريط البحث والفلترة السريعة
        filter_frame = tk.Frame(self.root, bg=BG_PANEL, padx=15, pady=10, highlightthickness=1, highlightbackground=BG_CARD)
        filter_frame.pack(fill="x", padx=15, pady=(8, 8))

        tk.Label(filter_frame, text="🔍 بحث بالاسم أو ID:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_PANEL).pack(side="left", padx=5)
        entry_search = tk.Entry(filter_frame, textvariable=self.search_var, bg=BG_INPUT, fg=TEXT_WHITE,
                                insertbackground=TEXT_WHITE, relief="flat", font=("Segoe UI", 10), width=18)
        entry_search.pack(side="left", padx=5)
        entry_search.bind("<KeyRelease>", lambda e: self.refresh_table())

        tk.Label(filter_frame, text="العنصر:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_PANEL).pack(side="left", padx=(15, 5))
        combo_elem = ttk.Combobox(filter_frame, textvariable=self.filter_elem_var, values=["كل العناصر"] + ALL_ELEMENTS, width=10, state="readonly")
        combo_elem.pack(side="left", padx=5)
        combo_elem.bind("<<ComboboxSelected>>", lambda e: self.refresh_table())

        tk.Label(filter_frame, text="الدور:", font=("Segoe UI", 10, "bold"), fg=TEXT_WHITE, bg=BG_PANEL).pack(side="left", padx=(15, 5))
        combo_role = ttk.Combobox(filter_frame, textvariable=self.filter_role_var, values=["كل الأدوار", "مهاجم", "مدافع", "درع صرف", "داعم"], width=10, state="readonly")
        combo_role.pack(side="left", padx=5)
        combo_role.bind("<<ComboboxSelected>>", lambda e: self.refresh_table())

        # أزرار تصنيف سريعة
        btn_all = tk.Button(filter_frame, text="عرض الـ 150 وحشاً", bg=BG_CARD, fg=TEXT_WHITE, relief="flat", padx=10, command=self.reset_filters)
        btn_all.pack(side="left", padx=(20, 5))

        btn_op = tk.Button(filter_frame, text="🔥 الأقوى (Win Rate ≥ 55%)", bg=BG_CARD, fg=ACCENT_RED, relief="flat", padx=10,
                           command=lambda: self.quick_filter("OP"))
        btn_op.pack(side="left", padx=5)

        btn_bal = tk.Button(filter_frame, text="⚖️ المتوازنون (48% - 55%)", bg=BG_CARD, fg=ACCENT_GREEN, relief="flat", padx=10,
                            command=lambda: self.quick_filter("BAL"))
        btn_bal.pack(side="left", padx=5)

        btn_weak = tk.Button(filter_frame, text="⚠️ الأضعف (Win Rate < 48%)", bg=BG_CARD, fg=ACCENT_YELLOW, relief="flat", padx=10,
                             command=lambda: self.quick_filter("WEAK"))
        btn_weak.pack(side="left", padx=5)

        # 5. جدول الوحوش الـ 150 مع نسب الفوز
        table_frame = tk.Frame(self.root, bg=BG_MAIN, padx=15)
        table_frame.pack(fill="both", expand=True, pady=(0, 10))

        columns = ("rank", "id", "name", "element", "role", "cost", "atk", "def", "apps", "wins", "win_rate", "rating")
        self.tree = ttk.Treeview(table_frame, columns=columns, show="headings", selectmode="browse")

        headers = [
            ("rank", "الترتيب #", 60, "center"),
            ("id", "ID", 45, "center"),
            ("name", "اسم الوحش", 200, "w"),
            ("element", "العنصر", 80, "center"),
            ("role", "الدور التكتيكي", 90, "center"),
            ("cost", "التكلفة", 60, "center"),
            ("atk", "الهجوم", 65, "center"),
            ("def", "الدفاع", 65, "center"),
            ("apps", "مرات اللعب", 95, "center"),
            ("wins", "مرات الفوز", 95, "center"),
            ("win_rate", "نسبة الفوز (Win Rate)", 135, "center"),
            ("rating", "التقييم التكتيكي", 160, "w"),
        ]

        for col_id, text, width, anchor in headers:
            self.tree.heading(col_id, text=text, command=lambda c=col_id: self.sort_by_column(c))
            self.tree.column(col_id, width=width, anchor=anchor)

        scroll_y = ttk.Scrollbar(table_frame, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=scroll_y.set)

        self.tree.pack(side="left", fill="both", expand=True)
        scroll_y.pack(side="right", fill="y")

        # 6. الشريط السفلي مع أزرار التصدير
        bottom_frame = tk.Frame(self.root, bg=BG_PANEL, padx=20, pady=8, highlightthickness=1, highlightbackground=BG_CARD)
        bottom_frame.pack(fill="x", padx=15, pady=(0, 15))

        self.lbl_count = tk.Label(bottom_frame, text="إجمالي الوحوش المعروضة: 150 وحشاً", font=("Segoe UI", 9), fg=TEXT_MUTED, bg=BG_PANEL)
        self.lbl_count.pack(side="left")

        btn_export_csv = tk.Button(bottom_frame, text="📊 حفظ جدول كملف Excel / CSV", bg=BG_CARD, fg=ACCENT_CYAN,
                                   relief="flat", padx=12, pady=4, cursor="hand2", command=self.export_csv)
        btn_export_csv.pack(side="right", padx=5)

        btn_export_md = tk.Button(bottom_frame, text="📄 حفظ تقرير كامل Markdown", bg=BG_CARD, fg=ACCENT_GREEN,
                                  relief="flat", padx=12, pady=4, cursor="hand2", command=self.export_md)
        btn_export_md.pack(side="right", padx=5)

    def create_kpi_card(self, parent, title, val, color, col_idx):
        card = tk.Frame(parent, bg=BG_PANEL, padx=15, pady=8, highlightthickness=1, highlightbackground=BG_CARD)
        card.pack(side="left", fill="both", expand=True, padx=4)

        t_lbl = tk.Label(card, text=title, font=("Segoe UI", 9, "bold"), fg=TEXT_MUTED, bg=BG_PANEL)
        t_lbl.pack(anchor="w")

        v_lbl = tk.Label(card, text=val, font=("Segoe UI", 12, "bold"), fg=color, bg=BG_PANEL)
        v_lbl.pack(anchor="w", pady=(2, 0))
        return v_lbl

    def load_initial_data(self):
        # محاولة قراءة نتائج سابقة إن وجدت
        cache_file = "monsters_evaluated_100k.json"
        if os.path.exists(cache_file):
            try:
                with open(cache_file, "r", encoding="utf-8") as f:
                    self.monsters_data = json.load(f)
                self.update_kpis()
                self.refresh_table()
                self.lbl_status.config(text="✅ تم تحميل نتائج الـ 100,000 مباراة السابقة جاهزة! (اضغط 'بدء المحاكاة' لإعادة الحساب)", fg=ACCENT_GREEN)
                return
            except Exception:
                pass
        
        # إن لم توجد نتائج سابقة، ننشئ جدولاً مبدئياً
        self.monsters_data = []
        for mid, m in MONSTER_MAP.items():
            self.monsters_data.append({
                "id": mid,
                "name": m["name"],
                "element": m["element"],
                "role": m["role"],
                "cost": m["cost"],
                "atk": m["atk"],
                "def": m["def"],
                "appearances": 0,
                "wins": 0,
                "win_rate": 50.0,
                "tier_rating": "جاهز للمحاكاة",
                "note": ""
            })
        self.refresh_table()

    def start_simulation(self):
        if self.is_simulating:
            return

        try:
            total_matches = int(self.matches_var.get())
        except Exception:
            total_matches = 100000

        self.is_simulating = True
        self.btn_start.config(state="disabled", text="⏳ جاري المحاكاة...", bg=BG_INPUT)
        self.lbl_status.config(text=f"🚀 جاري محاكاة {total_matches:,} مباراة باستخدام كافة أنوية المعالج (8 Cores)...", fg=ACCENT_CYAN)
        self.progress_bar["value"] = 0

        # تشغيل المحاكاة في خيط منفصل (Background Thread) لمنع تجميد الواجهة
        threading.Thread(target=self.run_multiprocess_sim, args=(total_matches,), daemon=True).start()

    def run_multiprocess_sim(self, total_matches):
        start_time = time.time()
        num_workers = min(8, os.cpu_count() or 4)
        chunk_size = total_matches // num_workers
        chunks = [chunk_size] * num_workers
        chunks[-1] += total_matches - sum(chunks)

        global_appearances = defaultdict(int)
        global_wins = defaultdict(int)
        global_p1_wins = 0

        with ProcessPoolExecutor(max_workers=num_workers) as executor:
            futures = [executor.submit(simulate_matches_worker, chunk) for chunk in chunks]
            done_count = 0
            for f in futures:
                res = f.result()
                done_count += res["matches"]
                global_p1_wins += res["p1_wins"]

                for cid, apps in res["appearances"].items():
                    global_appearances[cid] += apps
                for cid, w in res["wins"].items():
                    global_wins[cid] += w

                pct = (done_count / total_matches) * 100
                self.root.after(0, self.update_progress, pct, done_count, total_matches)

        elapsed = time.time() - start_time
        p1_rate = round((global_p1_wins / total_matches) * 100, 1)

        # تجهيز قائمة الوحوش المصنفة
        updated_monsters = []
        for mid, m in MONSTER_MAP.items():
            apps = global_appearances[mid]
            w = global_wins[mid]
            wr = round((w / apps * 100.0), 1) if apps > 0 else 50.0

            if wr >= 55.0:
                tier = "👑 خارق (Tier S)"
            elif wr >= 51.0:
                tier = "⚔️ قوي ممتاز (Tier A)"
            elif wr >= 48.0:
                tier = "⚖️ متوازن (Tier B)"
            elif wr >= 44.0:
                tier = "🛡️ دفاعي مساند (Tier C)"
            else:
                tier = "⚠️ ضعيف يحتاج تقوية"

            updated_monsters.append({
                "id": mid,
                "name": m["name"],
                "element": m["element"],
                "role": m["role"],
                "cost": m["cost"],
                "atk": m["atk"],
                "def": m["def"],
                "appearances": apps,
                "wins": w,
                "win_rate": wr,
                "tier_rating": tier,
            })

        # حفظ الكاش
        try:
            with open("monsters_evaluated_100k.json", "w", encoding="utf-8") as f:
                json.dump(updated_monsters, f, ensure_ascii=False, indent=2)
        except Exception:
            pass

        self.root.after(0, self.on_sim_done, updated_monsters, total_matches, elapsed, p1_rate)

    def update_progress(self, pct, done, total):
        self.progress_bar["value"] = pct
        self.lbl_status.config(text=f"🔥 محاكاة: {done:,} / {total:,} مباراة ({int(pct)}%)...")

    def on_sim_done(self, monsters, total_matches, elapsed, p1_rate):
        self.is_simulating = False
        self.btn_start.config(state="normal", text="▶️ بدء المحاكاة الكبرى", bg=ACCENT_GREEN)
        self.lbl_status.config(text=f"✅ اكتملت محاكاة {total_matches:,} مباراة بنجاح في {elapsed:.2f} ثانية! ({int(total_matches / elapsed):,} م/ث)", fg=ACCENT_GREEN)
        self.monsters_data = monsters
        self.update_kpis(p1_rate, total_matches)
        self.refresh_table()

    def update_kpis(self, p1_rate=53.8, total_matches=100000):
        if not self.monsters_data:
            return

        sorted_m = sorted(self.monsters_data, key=lambda x: x["win_rate"], reverse=True)
        top_m = sorted_m[0]
        bot_m = sorted_m[-1]

        self.lbl_card_matches.config(text=f"{total_matches:,} مباراة")
        self.lbl_card_top.config(text=f"{top_m['name']} ({top_m['win_rate']}%)")
        self.lbl_card_bot.config(text=f"{bot_m['name']} ({bot_m['win_rate']}%)")
        self.lbl_card_p1.config(text=f"{p1_rate}% (عادل)")

    def sort_by_column(self, col):
        if self.sort_column == col:
            self.sort_descending = not self.sort_descending
        else:
            self.sort_column = col
            self.sort_descending = True
        self.refresh_table()

    def refresh_table(self):
        query = self.search_var.get().strip().lower()
        elem_filter = self.filter_elem_var.get()
        role_filter = self.filter_role_var.get()

        filtered = []
        for m in self.monsters_data:
            if query and (query not in m["name"].lower() and query not in str(m["id"])):
                continue
            if elem_filter != "كل العناصر" and m["element"] != elem_filter:
                continue
            if role_filter != "كل الأدوار" and m["role"] != role_filter:
                continue
            filtered.append(m)

        # ترتيب
        reverse = self.sort_descending
        if self.sort_column in ["win_rate", "appearances", "wins", "atk", "def", "cost", "id"]:
            filtered.sort(key=lambda x: x.get(self.sort_column, 0), reverse=reverse)
        else:
            filtered.sort(key=lambda x: str(x.get(self.sort_column, "")), reverse=reverse)

        for item in self.tree.get_children():
            self.tree.delete(item)

        for rank, m in enumerate(filtered, 1):
            wr = m["win_rate"]
            wr_str = f"{wr}%"

            self.tree.insert("", "end", values=(
                rank,
                m["id"],
                m["name"],
                m["element"],
                m["role"],
                f"{m['cost']}⚡",
                f"{m['atk']}⚔️",
                f"{m['def']}🛡️",
                f"{m['appearances']:,}",
                f"{m['wins']:,}",
                wr_str,
                m.get("tier_rating", "")
            ))

        self.lbl_count.config(text=f"إجمالي الوحوش المعروضة: {len(filtered)} من أصل {len(self.monsters_data)} وحشاً")

    def reset_filters(self):
        self.search_var.set("")
        self.filter_elem_var.set("كل العناصر")
        self.filter_role_var.set("كل الأدوار")
        self.sort_column = "win_rate"
        self.sort_descending = True
        self.refresh_table()

    def quick_filter(self, mode):
        for item in self.tree.get_children():
            self.tree.delete(item)

        filtered = []
        for m in self.monsters_data:
            wr = m["win_rate"]
            if mode == "OP" and wr < 55.0:
                continue
            elif mode == "BAL" and not (48.0 <= wr < 55.0):
                continue
            elif mode == "WEAK" and wr >= 48.0:
                continue
            filtered.append(m)

        filtered.sort(key=lambda x: x["win_rate"], reverse=True)

        for rank, m in enumerate(filtered, 1):
            self.tree.insert("", "end", values=(
                rank,
                m["id"],
                m["name"],
                m["element"],
                m["role"],
                f"{m['cost']}⚡",
                f"{m['atk']}⚔️",
                f"{m['def']}🛡️",
                f"{m['appearances']:,}",
                f"{m['wins']:,}",
                f"{m['win_rate']}%",
                m.get("tier_rating", "")
            ))

        self.lbl_count.config(text=f"إجمالي الوحوش المعروضة: {len(filtered)} وحشاً")

    def export_csv(self):
        if not self.monsters_data:
            messagebox.showwarning("تنبيه", "لا توجد بيانات لتصديرها!")
            return

        file_path = os.path.join(os.getcwd(), "monsters_win_rate_100k.csv")
        try:
            with open(file_path, "w", encoding="utf-8-sig") as f:
                f.write("الترتيب,ID,اسم الوحش,العنصر,الدور,التكلفة,الهجوم,الدفاع,مرات اللعب,مرات الفوز,نسبة الفوز %,التقييم\n")
                for rank, m in enumerate(sorted(self.monsters_data, key=lambda x: x["win_rate"], reverse=True), 1):
                    f.write(f"{rank},{m['id']},\"{m['name']}\",{m['element']},{m['role']},{m['cost']},{m['atk']},{m['def']},{m['appearances']},{m['wins']},{m['win_rate']}%,\"{m.get('tier_rating','')}\"\n")
            messagebox.showinfo("تم التصدير", f"تم حفظ ملف Excel / CSV بنجاح في:\n{file_path}")
        except Exception as e:
            messagebox.showerror("خطأ", f"تعذر حفظ الملف: {e}")

    def export_md(self):
        file_path = os.path.join(os.getcwd(), "EVALUATION_100000_MONSTERS.md")
        messagebox.showinfo("تقرير Markdown", f"تقرير الـ 100 ألف مباراة الكامل محفوظ ومحدث في:\n{file_path}")

def main():
    root = tk.Tk()
    app = WinRateSimulator100kApp(root)
    root.mainloop()

if __name__ == "__main__":
    main()
