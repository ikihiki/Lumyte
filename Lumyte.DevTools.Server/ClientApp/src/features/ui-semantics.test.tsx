import {FluentProvider,webLightTheme} from '@fluentui/react-components';
import {render,within} from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {describe,expect,it,vi} from 'vitest';
import type {InputSnapshot} from '../protocol/types';
import {FeatureErrorBoundary} from '../components/FeatureErrorBoundary';
import {RecordTable} from '../components/StructuredDetails';
import {InputView} from './InputView';
import {DiagnosticsView} from './DiagnosticsView';
const wrap=(value:React.ReactNode)=>render(<FluentProvider theme={webLightTheme}>{value}</FluentProvider>);
const inputPayload:InputSnapshot={raw:{pressedKeys:[],pressedMouseButtons:[],pointerPosition:{},pointerDelta:{},wheelDelta:{},sources:[{source:'browser',deviceId:'browser',pressedKeys:[],pressedMouseButtons:[],pointerPosition:{},pointerDelta:{},wheelDelta:{}}]},actions:{maps:[{name:'Gameplay',priority:0,bindings:[{actionId:'game.jump',bindingId:'keyboard-space',control:'keyboard/Space',valueType:'Boolean'}]}],actions:[{id:'game.jump',valueType:'Boolean',value:null,phase:0}]}};
describe('typed feature presentation',()=>{
  it('renders the actual Input Monitor payload as source and action rows',()=>{
    const ui=within(wrap(<InputView invoke={vi.fn()} snapshot={inputPayload}/>).container);
    expect(ui.getByRole('table',{name:'Input sources'})).toHaveTextContent('browser');
    expect(ui.getByRole('table',{name:'Input actions'})).toHaveTextContent('game.jump');
    expect(ui.queryByText(/\{"/)).not.toBeInTheDocument();
  });
  it('renders the actual Action Maps payload as map and binding rows',async()=>{
    const ui=within(wrap(<InputView invoke={vi.fn()} snapshot={inputPayload}/>).container);
    await userEvent.click(ui.getByRole('tab',{name:'Action Maps'}));
    expect(ui.getByRole('table',{name:'Action maps'})).toHaveTextContent('Gameplay');
    expect(ui.getByRole('table',{name:'Action map bindings'})).toHaveTextContent('keyboard/Space');
  });
  it('keeps rendering a structured error for a malformed collection shape',()=>{
    const ui=within(wrap(<RecordTable items={{unexpected:true}} label="Input sources"/>).container);
    expect(ui.getByText('Input sources unavailable')).toBeInTheDocument();
    expect(ui.getByText('The runtime returned an unexpected data shape.')).toBeInTheDocument();
  });
  it('isolates a feature render exception and preserves its surrounding shell',()=>{
    const report=vi.spyOn(console,'error').mockImplementation(()=>{});
    const Broken=()=>{throw new Error('broken feature')};
    const ui=within(wrap(<><div>Application shell remains</div><FeatureErrorBoundary feature="Input" resetKey="input"><Broken/></FeatureErrorBoundary></>).container);
    expect(ui.getByText('Application shell remains')).toBeInTheDocument();
    expect(ui.getByText('Input could not be displayed')).toBeInTheDocument();
    expect(ui.getByRole('button',{name:'Try again'})).toBeInTheDocument();
    report.mockRestore();
  });
  it('presents activity tags and events semantically with raw details collapsed',async()=>{
    const ui=within(wrap(<DiagnosticsView invoke={vi.fn()} snapshot={{status:{},metrics:[],activities:[{activityId:'activity-123456789',traceId:'trace-123456789',source:'Lumyte.Resources',operation:'ResourceStore.Load',kind:'Internal',start:'2026-08-31T00:00:00Z',durationMilliseconds:12,status:'Ok',isActive:false,tags:[{key:'resource.key',value:'demo:scene'}],baggage:[],events:[{timestamp:'2026-08-31T00:00:00Z',name:'loaded',tags:[]}]}]}}/>).container);
    await userEvent.click(ui.getByRole('tab',{name:'Activities'}));
    expect(ui.getByRole('table',{name:'Activity tags and baggage'})).toBeInTheDocument();
    expect(ui.getByRole('table',{name:'Activity events'})).toBeInTheDocument();
    expect(ui.getAllByText('Raw details').every(value=>!value.closest('details')?.hasAttribute('open'))).toBe(true);
  });
});