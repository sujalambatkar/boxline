import { Component, Input } from '@angular/core';

import { DetentionResult, slabRange } from '../../../core/models/detention.models';
import { MOVEMENT_EVENT_LABELS } from '../../../core/models/tracking.models';

/** The priced period: key figures, the slab-by-slab breakdown and the tariff that was used. */
@Component({
  selector: 'app-detention-result',
  templateUrl: './detention-result.component.html',
  styleUrls: ['./detention-result.component.scss'],
})
export class DetentionResultComponent {
  @Input() result!: DetentionResult;

  readonly range = slabRange;

  get title(): string {
    return this.result.containerNumber ?? `${this.result.containerType} (what-if)`;
  }

  get startedByLabel(): string | null {
    return this.result.startedBy ? MOVEMENT_EVENT_LABELS[this.result.startedBy].toLowerCase() : null;
  }

  get hasCharge(): boolean {
    return this.result.slabs.length > 0;
  }
}
