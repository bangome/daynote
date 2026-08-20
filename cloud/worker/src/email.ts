import type { Env } from './env';

/**
 * Transactional email for password resets.
 *
 * MailChannels' free Cloudflare Workers integration ended in June 2024, so this needs a MailChannels
 * account and an API key like any other provider. The interface below is the whole surface, so
 * swapping to Resend or SES is one file.
 *
 * DKIM matters more than the provider choice: without it a reset code lands in spam, which reads to
 * the user as "the reset is broken". The DKIM fields are optional here only so a local `wrangler dev`
 * can run without a private key.
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
    super('No email sender is configured; set MAILCHANNELS_API_KEY and EMAIL_FROM.');
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

class MailChannelsSender implements EmailSender {
  constructor(
    private readonly apiKey: string,
    private readonly from: string,
    private readonly fromName: string,
    private readonly dkim?: { domain: string; selector: string; privateKey: string },
  ) {}

  async send(message: OutgoingEmail): Promise<void> {
    const personalization: Record<string, unknown> = {
      to: [{ email: message.to }],
    };

    if (this.dkim !== undefined) {
      personalization['dkim_domain'] = this.dkim.domain;
      personalization['dkim_selector'] = this.dkim.selector;
      personalization['dkim_private_key'] = this.dkim.privateKey;
    }

    const response = await fetch('https://api.mailchannels.net/tx/v1/send', {
      method: 'POST',
      headers: {
        'content-type': 'application/json',
        'X-Api-Key': this.apiKey,
      },
      body: JSON.stringify({
        personalizations: [personalization],
        from: { email: this.from, name: this.fromName },
        subject: message.subject,
        // Plain text only. An HTML reset email is a phishing template waiting to be copied, and
        // there is nothing here that benefits from markup.
        content: [{ type: 'text/plain', value: message.text }],
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
    typeof env.MAILCHANNELS_API_KEY !== 'string' ||
    env.MAILCHANNELS_API_KEY.length === 0 ||
    typeof env.EMAIL_FROM !== 'string' ||
    env.EMAIL_FROM.length === 0
  ) {
    throw new EmailNotConfiguredError();
  }

  const dkim =
    typeof env.DKIM_PRIVATE_KEY === 'string' && env.DKIM_PRIVATE_KEY.length > 0
      ? {
          domain: env.DKIM_DOMAIN ?? 'daynote.arachat.cc',
          selector: env.DKIM_SELECTOR ?? 'mailchannels',
          privateKey: env.DKIM_PRIVATE_KEY,
        }
      : undefined;

  return new MailChannelsSender(
    env.MAILCHANNELS_API_KEY,
    env.EMAIL_FROM,
    env.EMAIL_FROM_NAME ?? 'Daynote',
    dkim,
  );
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
