import type { Env } from './env';

/**
 * Transactional email for password resets, over Resend.
 *
 * Chosen over MailChannels because MailChannels' free Cloudflare Workers integration ended in June
 * 2024 and its replacement is a paid account. Resend's free tier (3,000/month, 100/day at the time of
 * writing) covers reset traffic for this app with room to spare.
 *
 * A side benefit worth stating: Resend holds the DKIM private key and hands you a public key to
 * publish, so no signing key lives in this Worker at all. The MailChannels path required us to store
 * one as a secret.
 *
 * The interface below is the whole surface, so moving again is one file.
 */
export interface EmailSender {
  send(message: OutgoingEmail): Promise<void>;
}

export interface OutgoingEmail {
  readonly to: string;
  readonly subject: string;
  readonly text: string;
}

export class EmailNotConfiguredError extends Error {
  constructor() {
    super('No email sender is configured; set RESEND_API_KEY and EMAIL_FROM.');
    this.name = 'EmailNotConfiguredError';
  }
}

export class EmailSendError extends Error {
  constructor(
    readonly status: number,
    body: string,
  ) {
    super(`The email provider rejected the message (${status}): ${body.slice(0, 200)}`);
    this.name = 'EmailSendError';
  }
}

class ResendSender implements EmailSender {
  constructor(
    private readonly apiKey: string,
    private readonly from: string,
    private readonly fromName: string,
  ) {}

  async send(message: OutgoingEmail): Promise<void> {
    const response = await fetch('https://api.resend.com/emails', {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        authorization: `Bearer ${this.apiKey}`,
      },
      body: JSON.stringify({
        from: `${this.fromName} <${this.from}>`,
        to: [message.to],
        subject: message.subject,
        // Plain text only. An HTML reset email is a phishing template waiting to be copied, and
        // nothing here benefits from markup.
        text: message.text,
      }),
    });

    if (!response.ok) {
      throw new EmailSendError(response.status, await response.text());
    }
  }
}

export function emailSenderFor(env: Env): EmailSender {
  if (env.EMAIL_SENDER !== undefined) {
    // Injected by tests, so the reset flow can be exercised without sending mail.
    return env.EMAIL_SENDER;
  }

  if (
    typeof env.RESEND_API_KEY !== 'string' ||
    env.RESEND_API_KEY.length === 0 ||
    typeof env.EMAIL_FROM !== 'string' ||
    env.EMAIL_FROM.length === 0
  ) {
    throw new EmailNotConfiguredError();
  }

  return new ResendSender(env.RESEND_API_KEY, env.EMAIL_FROM, env.EMAIL_FROM_NAME ?? 'Daynote');
}

/**
 * The reset email.
 *
 * It states the data consequence up front. Someone resetting a password is not expecting to lose
 * their notes, and finding that out afterwards would be the worst possible moment.
 */
export function resetEmail(to: string, code: string, minutes: number): OutgoingEmail {
  return {
    to,
    subject: 'Daynote password reset code',
    text: [
      'Your Daynote password reset code is:',
      '',
      `    ${code}`,
      '',
      `It expires in ${minutes} minutes and can be used once.`,
      '',
      'Important: resetting your password does not by itself unlock the notes in your',
      'cloud copy. Daynote encrypts them with a key derived from your password, and we',
      'cannot read or recover that key. After resetting you will be asked for your',
      'recovery key, or you can sign in on a device you have used before.',
      'The notes already on your PCs are not affected either way.',
      '',
      'If you did not request this you can ignore this email. Your password has not',
      'changed.',
    ].join('\n'),
  };
}
