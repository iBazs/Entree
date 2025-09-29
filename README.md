# Entrée

**Search. Compare. Automate. Your files, your rules.**

[![Open Source](https://img.shields.io/badge/Open%20Source-💚-success)](https://github.com) [![Fast](https://img.shields.io/badge/Performance-⚡%20Fast-blue)](https://github.com) [![Secure](https://img.shields.io/badge/Security-🔒%20Local%20First-orange)](https://github.com)



## ✨ Why Entree?

**Easy to Use** • Clean, intuitive interface that gets out of your way  
**Lightning Fast** • Search and compare thousands of files in seconds  
**Open Source** • Fully transparent, community-driven, and free forever  
**Intuitive** • No manual needed—just launch and go!






## 🔄 The Entree Workflow

### 1️⃣ **Search** → Generate detailed reports of your drives
### 2️⃣ **Compare** → Spot differences between reports from different times
### 3️⃣ **Tasks** → Automate the entire process with scheduled checks






## 🔍 Step 1: Search & Report

**Generate comprehensive reports of any folder with custom parameters**

### How It Works:
```
1. Open "Search Configuration"
2. Click "+ New" to create a search profile
3. Set "Search Path" → Point to your target drive/folder
4. Configure search parameters:
   • Search For: Files, folders, or specific patterns
   • Traversal Depth: How deep to scan subdirectories
   • Local or Domain Mode: For different scenarios
   • Inherited Access: Track permission inheritance across the system
5. Set "Save Path" → Where to store the report
6. Click "Save" → Your search configuration is ready!
7. Run the search → Entrée generates a detailed report
```

### 📊 What You Get:
- Complete file/folder inventory with paths
- Access permissions and ownership details
- Timestamps and metadata
- Exportable reports (Excel or CSV)
- Groups and members breakdown
- Access matrix visualization



## 📊 Step 2: Compare Reports

**Identify exactly what changed between two points in time**

### How It Works:
```
1. Navigate to "Result Comparison"
2. Click "Open File" under "Original File"
   → Select your baseline report (e.g., Jan_2025_audit.json)
3. Click "Open File" under "Compare File"
   → Select your recent report (e.g., Feb_2025_audit.json)
4. Click "Compare" → Differences appear instantly!
5. Review "Found Differences" table:
   • Path: Which files/folders changed
   • Difference: What specifically changed
6. Click "Export" → Save comparison results
```

### 🔍 What Gets Detected:
- **New files/folders** added since last scan
- **Deleted files/folders** removed since last scan
- **Permission changes** (who gained/lost access)
- **Modified files** with timestamp differences
- **Moved/renamed** items
- **Ownership transfers**



## ⏰ Step 3: Automate with Tasks

**Set it and forget it—automated searches run on your schedule**

### How It Works:
```
1. Go to "Scheduled Search"
2. Click "+ New Search Task"
3. Configure Task Information:
   • Task Name: "Weekly Security Audit"
   • Search File: Select your saved search configuration
4. Set Schedule Settings:
   • Schedule Type: Daily, Weekly, Monthly, or Custom
   • Date: Start date
   • Time: Exact execution time (24-hour format)
5. Configure Recurrence:
   • Recurrence Interval: Every N days/weeks/months
   • Weekly Schedule Days: Select specific days
   • Synchronize Time Zones: ON (for distributed teams)
6. Review "Command Preview" → See exactly what will run
7. Save the task → Entree handles the rest!
```

### 🎯 Task Capabilities:
- **Automated report generation** at scheduled intervals
- **Multi-timezone support** for global teams
- **Flexible recurrence** (daily, weekly, monthly, custom)
- **View all tasks** in "Searches Overview" (queued, ongoing, completed)
- **Cancel operations** anytime with one click
- **Historical results** always accessible



## 🔄 Complete Workflow Example

### Scenario: Monthly IT Audit with Change Tracking

**Month 1 - Establish Baseline:**
```
1. Search Configuration:
   - Name: "Monthly IT Audit"
   - Path: C:/CompanyData/
   - Depth: Full traversal
   - Save: Reports/January_2025.json

2. Run search → Generate baseline report

3. Create Task:
   - Name: "Monthly Audit Task"
   - Schedule: First day of each month at 01:00 AM
   - Uses: "Monthly IT Audit" configuration
```

**Month 2 - Automated Comparison:**
```
1. Task runs automatically → Generates February_2025.json

2. Compare Results:
   - Original: Reports/January_2025.json
   - Compare: Reports/February_2025.json
   - Click Compare

3. Review Changes:
   - 47 new files in Marketing folder
   - 3 deleted files from Archive
   - 12 permission changes in HR directory

4. Export Comparison → Send to management
```

**Ongoing - Set and Forget:**
```
✅ Reports generate automatically every month
✅ Historical data builds over time
✅ Compare any two months instantly
✅ Track trends and anomalies
✅ Zero manual intervention required
```

---

## 💬 Honest Talk

Created Entree because getting tired of:
- Searching for files manually across massive directory trees
- Wondering "what changed?" between system snapshots
- Paying for cloud-based audit tools that expose our data
- Clunky automation that breaks with every update

**Entree is different.** Fast, local, and yours.






## 🌟 Join the Journey

⭐ **Star us on GitHub** if Entree saves you time  
🐛 **Report issues** to help us improve  
🔧 **Contribute** and make Entree even better
