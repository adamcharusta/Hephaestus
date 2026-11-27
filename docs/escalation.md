# Escalation: a question for the Owner

When Hephaestus cannot go on with a Task, it asks the Owner one specific question on the Task's issue and takes the next Task. The Task
waits in `waiting-for-owner`, and no other Task is held back by it. This is called an **Escalation**.

## What raises one

There is one path in Core for it, and everything that cannot go on takes it:

- **A decision only the Owner can make.** Later stories raise it from an Agent's result that needs a decision, a blocked push, and the like.
  Today nothing in v0.1 raises one of these yet; the path is there for them.
- **An effect on GitHub that reached its bound.** A label that could not be made after the retries (`effectRetries`), or that GitHub refused for
  good, raises the same Escalation, and the comment says which effect could not be completed and why
  ([`state-labels.md`](state-labels.md)).

A Task that is in `waiting-for-owner` already, or that has ended, is not escalated again: **one Escalation asks for one decision.**

## What happens, in one transaction

The loop records the Escalation on the Task, moves the Task to `waiting-for-owner`, and journals the intents of three effects, which are made
after the commit and are resolved after a crash by reading, as every effect is:

| Effect | What it does |
| --- | --- |
| label | the issue shows `hephaestus:waiting-for-owner` |
| comment | the question is posted on the issue (below) |
| notification | `INotifier` is called, once |

The Escalation holds the decision, the options, the condition that ends the wait (`trusted-reply`: a reply from a Trusted User, below), what
caused it, and, for an effect that could not be completed, which effect and why. The database allows a Task one open
Escalation at a time.

## The comment

```text
**Hephaestus needs a decision.**

Which database should the login page use?
A. SQLite, as the rest does
B. Postgres

Reply on this issue. Any reply from a Trusted User returns the Task to the Queue with the answer.
```

The headline, the lettered options one per line, and how to answer are fixed sentences from the `en` and `pl` resources, in the language of the
text on GitHub (the Task's, else the Project's, else the installation's). The decision is one sentence and each option is one line; text
over 500 and 300 characters is cut with an ellipsis. The hidden marker `<!-- hephaestus:effect=<key> -->` is the last line, so that the comment is
found again after a crash and is posted once.

**Text that came from an Agent is neutralised before it is posted**, so that it cannot act on GitHub in Hephaestus's name:

- HTML comments are escaped (`<!--` is shown as text), so the text cannot forge the hidden marker or hide content;
- `@mentions` are broken with a zero-width space, so nobody is notified by it;
- issue-closing keywords (`closes #12`, `fixes owner/repo#3`) are broken, so the comment closes no issue;
- a `/hephaestus` command is shown as code, to the end of its line.

Stored secret values are masked as well, as in every comment.

## The notification

`INotifier` is called when an Escalation is raised. In v0.1 the implementation in Host does nothing: **the comment on the issue is how you are
told**, and email or a phone push is a later release. What the notifier does, including failing, changes nothing about the Task. The call is
journaled before it is made and is never made twice: after a restart, a notification that was started and has no result is ended as
`gave-up` (`interrupted`), not sent again. A failing notifier is one error line in the log, with the effect's key.

## Answering

A reply on the issue from a Trusted User, made after the Escalation was raised, **is the answer**. In one transaction the loop admits the comment
and records it with the id of the Escalation it answers, ends the Escalation as `answered`, moves the Task back to `queued`, and journals the
intent of the label that shows it. The answer is then among the Task's admitted comments, which are what an Agent may be given as context.
Nothing else is posted: the label on the issue is the acknowledgement.

- **Anyone else's comment does nothing.** A comment from a user who is not a Trusted User is recorded as a rejected Signal with that user's
  numeric id and gets no reply; the Task keeps waiting. The comments of Hephaestus's own account are never admitted.
- **A Trusted User's comment on a Task that is not waiting** is admitted as context for later steps, with its hash, and changes no state.
  So is one made before the Escalation was raised, which cannot answer it.
- **A command is not an answer.** A comment whose first line begins with `/hephaestus` is neither an answer nor context; what a command does is
  for the story that handles commands.
- **An edited comment is not used.** The event data does not say who edited a comment, so a comment that had been edited by the time Hephaestus
  read it carries no text and admits nothing. Reply again with a new comment. The next story of the epic decides which edits are accepted.
- **The wait is one answer long.** The first reply that counts resumes the Task; a second reply while it is queued is context only.

## Ending the wait without an answer

A Trusted User who removes the Marker Label from the issue of a Task in `waiting-for-owner` makes it `cancelled`; one who closes the issue
makes it `closed`. The Escalation is closed with that outcome, and the issue shows the new state. The same actions by anyone else change
nothing. Withdrawing and closing Tasks in other states are later stories.
