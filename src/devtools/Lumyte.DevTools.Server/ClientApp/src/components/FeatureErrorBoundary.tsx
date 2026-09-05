import {Component,type ErrorInfo,type ReactNode} from 'react';
import {Button,MessageBar,MessageBarActions,MessageBarBody,MessageBarTitle} from '@fluentui/react-components';

interface Props { feature:string; resetKey:string; children:ReactNode }
interface State { error?:Error }

export class FeatureErrorBoundary extends Component<Props,State>{
  state:State={};
  static getDerivedStateFromError(error:Error):State{return{error}}
  componentDidCatch(error:Error,info:ErrorInfo){console.error(`Failed to render ${this.props.feature}.`,error,info)}
  componentDidUpdate(previous:Props){if(previous.resetKey!==this.props.resetKey&&this.state.error)this.setState({error:undefined})}
  render(){if(!this.state.error)return this.props.children;return <MessageBar intent="error"><MessageBarBody><MessageBarTitle>{this.props.feature} could not be displayed</MessageBarTitle>{this.state.error.message}</MessageBarBody><MessageBarActions><Button onClick={()=>this.setState({error:undefined})}>Try again</Button></MessageBarActions></MessageBar>}
}
