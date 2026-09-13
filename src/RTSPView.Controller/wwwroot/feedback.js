(() => {
  const trigger = document.getElementById('shareFeedback');
  const stylesheet = document.createElement('link');
  stylesheet.rel = 'stylesheet';
  stylesheet.href = 'feedback.css?v=1';
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
    <p class="feedback-note">Issues are public. Remove passwords, stream credentials, and private details from screenshots or logs before posting.</p>
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
