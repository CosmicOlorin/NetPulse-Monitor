# NetPulse Monitor 1.0.17

## Correct SMS conversations

- Restores complete chronological conversation threads instead of displaying
  only the first matching message.
- Keeps Inbox and Sent messages together by normalized phone identity, including
  national, `+` and `00` country-code variants.
- Removes drafts from conversation grouping and conversation search.
- Adds a dedicated **Drafts** view. Selecting a draft loads its recipient and
  content into the composer so it can be reviewed, sent or deleted.
- Keeps **Timeline** as a separate newest-first Inbox/Sent view.
- Repairs keyboard and mouse conversation selection and preserves the active
  thread after read-status refreshes.

## Android Companion parity

- Replaces the mixed flat SMS list with separate **Conversations** and
  **Drafts** views.
- Displays the complete Inbox/Sent thread as message bubbles and keeps message
  actions attached to the selected message.
- Preserves normalized-number grouping, saved contact names, mark-read, delete,
  compose and send behavior.

## Packaging

- Publishes the matching Android Companion APK together with the Windows
  application. The Windows ZIP contains both the stable desktop executable and
  the APK served by the local Companion setup.
- No router credentials, SMS contents, contacts, LTE history or other local user
  data are included in release artifacts.
