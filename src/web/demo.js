// Demo host for ui.html?demo=1: answers the page with sample data, for screenshots and visual QA.
(function () {
  const listeners = [];
  const emit = (data, delay = 250) => setTimeout(() => listeners.forEach(fn => fn({ data })), delay);
  const now = Math.floor(Date.now() / 1000);
  const courses = [
    { id: 101, name: 'חשבון אינפיניטסימלי 2', category: 'מדעי המחשב · שנה א', current: true, local: { exists: true, studyPages: 6, exams: 8 } },
    { id: 102, name: 'מבני נתונים', category: 'מדעי המחשב · שנה א', current: true, local: { exists: true, studyPages: 3, exams: 4 } },
    { id: 103, name: 'פיזיקה 1', category: 'הנדסת חשמל · שנה א', current: true, local: { exists: false } },
    { id: 104, name: 'אלגברה לינארית 2', category: 'מדעי המחשב · שנה א', current: true, local: { exists: true, studyPages: 1 } },
    { id: 105, name: 'מבוא למדעי המחשב', category: 'שנה קודמת', current: false, local: { exists: true, studyPages: 11 } }
  ];
  const events = [
    { name: 'תרגיל בית 5', course: 'חשבון אינפיניטסימלי 2', when: now + 26 * 3600, url: '', overdue: false },
    { name: 'מטלה 3: עצי AVL', course: 'מבני נתונים', when: now + 3 * 86400, url: '', overdue: false },
    { name: 'בוחן אמצע', course: 'פיזיקה 1', when: now + 9 * 86400, url: '', overdue: false },
    { name: 'תרגיל 4 - ערכים עצמיים', course: 'אלגברה לינארית 2', when: now + 12 * 86400, url: '', overdue: false }
  ];
  const lec = (num, title, items, local) => ({ num, title, items, local: Object.assign({ dir: 'D:\\HIT\\Courses\\x\\שיעור ' + num, exists: false, files: 0, videos: 0, transcripts: 0, studyPage: null }, local || {}) });
  const sections = [
    lec(0, 'מידע כללי', [{ mod: 'resource', name: 'סילבוס הקורס תשפ"ו' }, { mod: 'forum', name: 'הודעות' }]),
    lec(1, 'אינטגרל רימן', [{ mod: 'resource', name: 'הרצאה 1 - אינטגרל רימן' }, { mod: 'assign', name: 'תרגיל בית 1', dates: 'הגשה: 12 בנובמבר' }, { mod: 'zoom', name: 'הקלטת ההרצאה' }], { exists: true, files: 4, videos: 1, transcripts: 1, studyPage: 'x', lecturePdf: 'x' }),
    lec(2, 'המשפט היסודי', [{ mod: 'resource', name: 'הרצאה 2 - המשפט היסודי' }, { mod: 'resource', name: 'תרגול 2' }, { mod: 'zoom', name: 'הקלטה' }], { exists: true, files: 3 }),
    lec(3, 'שיטות אינטגרציה', [{ mod: 'resource', name: 'הרצאה 3' }, { mod: 'assign', name: 'תרגיל בית 3' }]),
    lec(4, 'אינטגרלים מוכללים', [{ mod: 'resource', name: 'הרצאה 4' }, { mod: 'url', name: 'קישור להקלטה' }])
  ];
  const ai = { provider: 'claude', ready: true, setup: '', claude: { installed: true, loggedIn: true, email: 'student@example.com', plan: 'pro' }, gemini: { installed: false, hasKey: false } };
  const info = { lecturer: 'ד"ר כהן', officeHours: 'יום ב׳ 12:00-13:00', homeworkRequired: true, homeworkNote: '10% מהציון, צריך להגיש 8 מתוך 10',
    examFormat: '3 שעות, 5 שאלות פתוחות', materialsAllowed: 'דף נוסחאות אישי', attendance: 'לא חובה',
    grading: [{ component: 'מבחן סופי', weight: 80 }, { component: 'תרגילי בית', weight: 10 }, { component: 'בוחן אמצע', weight: 10 }],
    importantDates: [{ date: '2026-12-10', what: 'בוחן אמצע' }, { date: '2027-02-01', what: 'מועד א' }], tips: ['המרצה אוהב לשאול על התכנסות אינטגרלים מוכללים', 'בתרגילים - חובה לנמק כל מעבר'] };

  window.DemoEmit = emit;
  window.DemoHost = {
    addEventListener: (t, fn) => listeners.push(fn),
    postMessage: (m) => {
      switch (m.type) {
        case 'ready':
          emit({ type: 'settings', studyRoot: 'D:\\HIT\\Courses', recordingsDir: 'D:\\HIT\\Zoom Recordings', notabilityDir: '', aiProvider: 'claude', hasGeminiKey: false, onboarded: !location.search.includes('wizard'), userName: 'נועה לוי', version: '2.0.0' });
          emit({ type: 'aiStatus', status: ai }, 500);
          emit({ type: 'moodle', state: 'ready' }, 700);
          emit({ type: 'home', events, name: 'נועה לוי' }, 1100);
          emit({ type: 'courses', data: courses }, 1300);
          emit({ type: 'job', id: 1, name: 'שיעור 2', state: 'running', status: 'מוריד וידאו · 46% מתוך 273MiB · נשארו 00:14', progress: 46 }, 1400);
          emit({ type: 'job', id: 2, name: 'שיעור 1', state: 'done', status: 'הווידאו והתמלול נשמרו', progress: 100, hasVideo: true, hasTranscript: true }, 1450);
          break;
        case 'loadCourses': emit({ type: 'courses', data: courses }, 600); break;
        case 'openCourse': {
          const c = courses.find(x => x.id === m.id);
          emit({ type: 'course', id: m.id, name: c.name, folder: 'D:\\HIT\\Courses\\' + c.name, sections, lecturer: 'ד"ר כהן', lecturerGuess: [],
            local: { dir: 'x', exists: true, exams: 8, notes: 12, studyPages: 6, info, examPrep: 'x', lecturerProfile: 'x', courseInfoMd: 'x' },
            notability: { configured: true, linked: 'אינפי 2', subjects: ['אינפי 2', 'מבני נתונים', 'פיזיקה'], notes: 12 } }, 900);
          emit({ type: 'newMaterial', course: c.name, items: [{ num: 4, title: 'אינטגרלים מוכללים', name: 'תרגיל בית 4', mod: 'assign' }, { num: 4, title: 'אינטגרלים מוכללים', name: 'פתרון תרגיל 3', mod: 'resource' }] }, 1300);
          break;
        }
        case 'lecturePdf': {
          const steps = ['קורא את חומרי הקורס', 'כותב את הסיכום', 'מסמן רמזים בדפי התרגיל', 'מכין את קובץ ה-PDF...'];
          steps.forEach((t, i) => emit({ type: 'sectionStatus', course: m.course, num: m.num, text: t, busy: true }, 300 + i * 700));
          emit({ type: 'sectionStatus', course: m.course, num: m.num, text: 'קובץ ה-PDF של ההרצאה מוכן ✨', busy: false }, 3300);
          break;
        }
        case 'chat':
          emit({ type: 'chatTool', course: m.course, text: 'קורא שיעור 4/הרצאה 4.pdf' }, 600);
          emit({ type: 'chat', course: m.course, role: 'assistant', text: 'בשיעור 4 למדתם על **אינטגרלים מוכללים**:\n- סוג ראשון: גבול אינסופי\n- סוג שני: פונקציה לא חסומה\n\nהמרצה מדגיש תמיד את **מבחן ההשוואה**. רוצה שנעבור על שאלה 2 מתרגיל הבית?' }, 1800);
          emit({ type: 'chatDone', course: m.course }, 1900);
          break;
        case 'syncSection':
          emit({ type: 'sectionStatus', course: m.course, num: m.section.num, text: 'מוריד 2/3: הרצאה', busy: true }, 200);
          emit({ type: 'sectionStatus', course: m.course, num: m.section.num, text: 'נשמרו 3 קבצים · הקלטה אחת בדרך', busy: false }, 1600);
          break;
      }
    }
  };
})();
