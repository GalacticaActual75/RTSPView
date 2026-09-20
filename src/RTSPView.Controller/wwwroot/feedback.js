(() => {
  const trigger = document.getElementById('shareFeedback');
  const support = document.createElement('nav'); support.className = 'floating-support'; support.setAttribute('aria-label', 'Feedback and support');
  const coffee = document.createElement('a'); coffee.className = 'support-fab coffee-fab'; coffee.href = 'https://buymeacoffee.com/galacticaactual75'; coffee.target = '_blank'; coffee.rel = 'noopener noreferrer'; coffee.title = 'Buy me a coffee'; coffee.setAttribute('aria-label', 'Buy me a coffee (opens a new tab)');
  coffee.innerHTML = '<img src="support-coffee.png" alt="" width="48" height="48">';
  trigger.className = 'support-fab feedback-fab'; trigger.title = 'Submit feedback'; trigger.setAttribute('aria-label', 'Submit feedback'); trigger.setAttribute('aria-haspopup', 'dialog');
  trigger.innerHTML = '<svg viewBox="0 0 32 32" aria-hidden="true"><path fill="currentColor" d="M16 4C8.8 4 3 8.7 3 14.5S8.8 25 16 25c1.6 0 3.2-.2 4.6-.7L27 28l-1.4-7C27.7 19.1 29 16.9 29 14.5 29 8.7 23.2 4 16 4Z"/><g fill="#259bd2"><circle cx="10" cy="14" r="1.6"/><circle cx="16" cy="14" r="1.6"/><circle cx="22" cy="14" r="1.6"/></g></svg>';
  support.append(coffee, trigger); document.getElementById('app').append(support);
  const stylesheet = document.createElement('link');
  stylesheet.rel = 'stylesheet';
  stylesheet.href = 'feedback.css?v=2';
  document.head.append(stylesheet);
  const dialog = document.createElement('dialog');
  dialog.id = 'feedbackDialog';
  dialog.setAttribute('aria-labelledby', 'feedbackTitle');
  dialog.innerHTML = `
    <form method="dialog" class="feedback-heading">
      <h2 id="feedbackTitle">Share feedback</h2>
      <button class="secondary" autofocus>Close</button>
    </form>
    <p>Found a bug or have an idea for RTSPView? Share it on our GitHub repository.</p>
    <ol>
      <li>Sign in to GitHub, or create a free GitHub account.</li>
      <li>Check existing issues to see whether someone has already reported it. Add a comment there if they have.</li>
      <li>Open a new issue with a clear title. Describe your idea, or explain what happened, what you expected, and how to reproduce it. Include your RTSPView version and screenshots when helpful.</li>
    </ol>
    <p class="feedback-note">Issues and uploaded attachments are public. Use a synthetic example or crop to the control you are reporting. Remove camera pictures, location and camera names, IP addresses, hostnames, schedules, file paths, passwords and stream credentials before uploading. Do not attach configuration exports. Closing an issue or removing an image from its description does not necessarily delete the uploaded file.</p>
    <div class="feedback-links">
      <a class="feedback-primary" href="https://github.com/GalacticaActual75/RTSPView/issues/new/choose" target="_blank" rel="noopener noreferrer">Open a new issue ↗</a>
      <a href="https://github.com/GalacticaActual75/RTSPView/issues" target="_blank" rel="noopener noreferrer">Browse existing issues ↗</a>
    </div>
    <section class="feedback-support" aria-labelledby="feedbackSupportTitle">
      <h3 id="feedbackSupportTitle">Enjoying RTSPView?</h3>
      <p>Shameless plug: buy me a coffee and help fuel the next improvement. Feedback is always welcome, coffee or no coffee.</p>
      <a class="donate-button" href="https://buymeacoffee.com/galacticaactual75" target="_blank" rel="noopener noreferrer">Buy me a coffee ↗</a>
    </section>`;
  document.body.append(dialog);
  trigger.addEventListener('click', () => dialog.showModal());
  dialog.addEventListener('close', () => trigger.focus());
})();
